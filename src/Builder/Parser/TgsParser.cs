using typegen.Builder.Lexer;
using typegen.Builder.Types;

namespace typegen.Builder.Parser;

/// <summary>
/// Represents a parsing error with location information for IDE integration.
/// </summary>
public record ParseError(string FilePath, int Line, int Column, string Message)
{
    public override string ToString()
    {
        return $"{FilePath}:{Line}: {Message}";
    }

    public string ToStringWithColumn()
    {
        return $"{FilePath}:{Line}:{Column}: {Message}";
    }

    public string ToStringDaemon()
    {
        return $"{Line}<SPACE>{Message}";
    }
}

/// <summary>
/// Recursive descent parser for the TGS (TypeGen Schema) language.
/// Consumes tokens from the lexer and builds an Abstract Syntax Tree (AST)
/// represented by SchemaFile objects containing schemas, enums, imports, and variable paths.
/// 
/// The parser handles:
/// - Import statements: import { Name1, Name2 } from "./file.tgs";
/// - Variable path assignments: varName = /Path; or varName = otherVar + /Path;  
/// - Root path declarations: rootPath = /Root;
/// - Schema definitions: create schema Name&lt;path&gt;( prop: type; );
/// - Enum definitions: create enum Name&lt;path&gt;( Value1, Value2 );
/// - Inheritance: create schema Child&lt;path&gt; & Parent ( ... );
/// - Generic types: Array&lt;string&gt;, Map&lt;string, number&gt;
/// 
/// Features comprehensive error collection and recovery:
/// - Collects ALL errors in a file instead of stopping at the first
/// - Provides VSCode-compatible error formatting for Ctrl+click navigation
/// - Attempts to continue parsing after errors for better error reporting
/// </summary>
public class TgsParser
{
    private readonly List<Token> _tokens;
    private int _current = 0;
    private readonly string _filePath;
    private readonly List<ParseError> _errors = [];

    /// <summary>
    /// Creates a new parser for the given tokens.
    /// Filters out whitespace and comments since they're not needed for parsing.
    /// </summary>
    /// <param name="tokens">Tokens from the lexer</param>
    /// <param name="filePath">Path to the source file being parsed (for error reporting)</param>
    public TgsParser(List<Token> tokens, string filePath)
    {
        // Remove whitespace and comments - they're only needed for error reporting
        _tokens = tokens.Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comment).ToList();
        _filePath = filePath;
    }

    /// <summary>
    /// Main parsing method. Parses the entire file and returns a SchemaFile object.
    /// Collects all errors and throws an aggregated exception if any errors are found.
    /// 
    /// Grammar overview:
    /// File := (Import | VariableAssignment | RootPath | SchemaOrEnum)*
    /// Import := 'import' '{' IdentifierList '}' 'from' StringLiteral ';'
    /// VariableAssignment := Identifier '=' PathExpression ';'
    /// RootPath := 'rootPath' '=' Path ';'
    /// SchemaOrEnum := 'create' ('schema' | 'enum') Definition
    /// </summary>
    /// <returns>Parsed schema file containing all definitions</returns>
    /// <exception cref="Exception">Thrown if parsing errors are encountered</exception>
    public SchemaFile Parse(bool lsp = false)
    {
        var imports = new List<Import>();
        var variablePaths = new List<VariablePath>();
        var schemas = new List<Schema>();
        var enums = new List<TgEnum>();
        string? rootPath = null;

        while (!IsAtEnd())
        {
            // Skip newlines at the top level
            if (Check(TokenType.Newline))
            {
                Advance();
                continue;
            }

            try
            {
                // Parse different top-level constructs
                if (Check(TokenType.Import))
                {
                    var import = ParseImport();
                    if (import != null) imports.Add(import);
                }
                else if (CheckIdentifier("rootPath"))
                {
                    var parsed = ParseRootPath();
                    if (parsed != null) rootPath = parsed;
                }
                else if (Check(TokenType.Identifier))
                {
                    // Check if this looks like an orphaned property instead of a variable assignment
                    if (PeekNext()?.Type == TokenType.Colon)
                    {
                        var propName = Peek().Value;
                        AddError($"Orphaned property '{propName}'. Properties must be defined inside a schema");
                        SkipToEndOfOrphanedProperty();
                    }
                    else
                    {
                        // Variable path assignment
                        var variable = ParseVariableAssignment(variablePaths);
                        if (variable != null) variablePaths.Add(variable);
                    }
                }
                else if (Check(TokenType.Create))
                {
                    Advance(); // consume 'create'

                    if (Check(TokenType.Schema))
                    {
                        var schema = ParseSchema(rootPath, variablePaths);
                        if (schema != null) schemas.Add(schema);
                    }
                    else if (Check(TokenType.Enum))
                    {
                        var enumDef = ParseEnum(rootPath, variablePaths);
                        if (enumDef != null) enums.Add(enumDef);
                    }
                    else
                    {
                        // Check if this looks like a missing enum/schema keyword
                        if (Check(TokenType.Identifier))
                        {
                            var identifier = Peek().Value;
                            AddError($"Missing 'enum' or 'schema' keyword after 'create'. Did you mean 'create enum/schema {identifier}'?");
                        }
                        else
                        {
                            AddError($"Expected 'schema' or 'enum' after 'create', got {Peek().Type}");
                        }

                        // Use specialized recovery for create statements
                        SkipToEndOfCreate();
                    }
                }
                else
                {
                    AddError($"Unexpected token: {Peek()}");
                    // Try to recover by skipping this token
                    Advance();
                }
            }
            catch (Exception)
            {
                // Error already added, try to recover
                SkipToNextStatement();
            }
        }

        // Validate schema inheritance after parsing all schemas
        ValidateInheritance(schemas, imports);

        // Validate property types after parsing all schemas and enums
        ValidatePropertyTypes(schemas, imports, enums);

        // If we have errors, throw them all
        if (_errors.Count > 0)
        {   
            var errorMessage = lsp switch
            {
                true => string.Join("<ERROR>", _errors.Select(e => e.ToStringDaemon())),
                false => "Parsing failed with the following errors:<ERRORTITLE>" + string.Join("<ERROR>", _errors.Select(e => e.ToString()))
            };
            throw new Exception(errorMessage);
        }

        return new SchemaFile(_filePath, rootPath, variablePaths, schemas, imports, enums);
    }

    #region Inheritance Validation

    /// <summary>
    /// Validates schema inheritance rules after all schemas have been parsed.
    /// Checks for:
    /// 1. Self-inheritance (schema inheriting from itself)
    /// 2. Invalid inheritance targets (schema doesn't exist in current file or imports)
    /// </summary>
    /// <param name="schemas">All schemas defined in the current file</param>
    /// <param name="imports">All imports that may contain schemas</param>
    private void ValidateInheritance(List<Schema> schemas, List<Import> imports)
    {
        // Build a set of available schema names from current file
        var localSchemaNames = schemas.Select(s => s.Name).ToHashSet();

        // Build a set of available schema names from imports
        var importedSchemaNames = new HashSet<string>();
        foreach (var import in imports)
        {
            foreach (var importName in import.ImportNames)
            {
                importedSchemaNames.Add(importName);
            }
        }

        // Combine all available schema names
        var allAvailableSchemas = new HashSet<string>(localSchemaNames);
        allAvailableSchemas.UnionWith(importedSchemaNames);

        // Validate each schema's inheritance
        foreach (var schema in schemas)
        {
            if (string.IsNullOrEmpty(schema.InheritanceName)) continue;

            var inheritanceName = schema.InheritanceName;

            // Check for self-inheritance
            if (inheritanceName == schema.Name)
            {
                AddErrorForSchema(schema, $"Schema '{schema.Name}' cannot inherit from itself");
                continue;
            }

            // Check if inheritance target exists
            if (!allAvailableSchemas.Contains(inheritanceName))
            {
                AddErrorForSchema(schema,
                    $"Schema '{schema.Name}' cannot inherit from '{inheritanceName}' because it is not defined. " +
                    $"Make sure '{inheritanceName}' is defined in this file or imported from another file.");
            }
        }
    }

    /// <summary>
    /// Adds an error for a specific schema. Since we don't have token position information
    /// after parsing, we add a general error for the schema.
    /// </summary>
    /// <param name="schema">The schema that has the error</param>
    /// <param name="message">Error message</param>
    private void AddErrorForSchema(Schema schema, string message)
    {
        // Since we don't have token position after parsing, we use line 0
        // The error will still be helpful for identifying the problematic schema
        _errors.Add(new ParseError(_filePath, 0, 0, message));
    }

    #endregion

    #region Type Validation

    /// <summary>
    /// Returns the set of built-in types supported by the TGS language.
    /// These types are always available and don't need to be imported or defined.
    /// </summary>
    /// <returns>HashSet containing all built-in type names</returns>
    private static HashSet<string> GetBuiltInTypes()
    {
        return
        [
            "Uid", "int", "uint", "long", "ulong", "short", "ushort",
            "byte", "sbyte", "float", "double", "decimal", "bool",
            "char", "object", "string", "Array", "List", "Map",
            "Set", "Queue", "Date", "DateTime"
        ];
    }

    /// <summary>
    /// Validates all property types in all schemas to ensure they reference valid types.
    /// Valid types include:
    /// 1. Built-in types (Uid, string, int, Array, etc.)
    /// 2. Schemas defined in the current file
    /// 3. Enums defined in the current file  
    /// 4. Schemas and enums imported from other files
    /// 5. Generic types with valid type parameters (Array&lt;string&gt;, Map&lt;string, Customer&gt;)
    /// </summary>
    /// <param name="schemas">All schemas defined in the current file</param>
    /// <param name="imports">All imports that may contain schemas and enums</param>
    /// <param name="enums">All enums defined in the current file</param>
    private void ValidatePropertyTypes(List<Schema> schemas, List<Import> imports, List<TgEnum> enums)
    {
        // Build set of all available type names
        var availableTypes = GetBuiltInTypes();

        // Add local schema names
        foreach (var schema in schemas)
        {
            availableTypes.Add(schema.Name);
        }

        // Add local enum names
        foreach (var enumDef in enums)
        {
            availableTypes.Add(enumDef.Name);
        }

        // Add imported schema and enum names
        foreach (var import in imports)
        {
            foreach (var importName in import.ImportNames)
            {
                availableTypes.Add(importName);
            }
        }

        // Validate each property in each schema
        foreach (var schema in schemas)
        {
            foreach (var property in schema.Props)
            {
                if (!IsValidType(property.Type, availableTypes))
                {
                    // Use the precise location of the type for the error message
                    _errors.Add(new ParseError(_filePath, property.TypeLine, property.TypeColumn,
                        $"Property '{property.Name}' in schema '{schema.Name}' has invalid type '{property.Type}'. " +
                        $"Type must be a built-in type, defined schema/enum, or imported type."));
                }
            }
        }
    }

    /// <summary>
    /// Validates whether a type name is valid, including support for generic types.
    /// Recursively validates generic type parameters.
    /// 
    /// Examples of valid types:
    /// - string (built-in)
    /// - Customer (defined schema)
    /// - CustomerStatus (defined enum)
    /// - Array&lt;string&gt; (generic with valid parameter)
    /// - Map&lt;string, Customer&gt; (generic with multiple valid parameters)
    /// - Array&lt;Map&lt;string, Customer&gt;&gt; (nested generics)
    /// </summary>
    /// <param name="typeName">The type name to validate</param>
    /// <param name="availableTypes">Set of all available type names</param>
    /// <returns>True if the type is valid, false otherwise</returns>
    private bool IsValidType(string typeName, HashSet<string> availableTypes)
    {
        // Handle simple types
        if (!typeName.Contains('<'))
        {
            return availableTypes.Contains(typeName);
        }

        // Handle generic types like Array<string> or Map<string, Customer>
        var match = System.Text.RegularExpressions.Regex.Match(typeName, @"^(\w+)<(.+)>$");
        if (!match.Success)
        {
            return false; // Malformed generic type
        }

        var baseType = match.Groups[1].Value;
        var typeParameters = match.Groups[2].Value;

        // Validate the base type (e.g., "Array" in "Array<string>")
        if (!availableTypes.Contains(baseType))
        {
            return false;
        }

        // Parse and validate each type parameter
        var parameterTypes = ParseGenericTypeParameters(typeParameters);
        foreach (var paramType in parameterTypes)
        {
            if (!IsValidType(paramType.Trim(), availableTypes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses generic type parameters from a string, handling nested generics correctly.
    /// 
    /// Examples:
    /// - "string" -> ["string"]
    /// - "string, number" -> ["string", "number"]
    /// - "string, Map&lt;string, Customer&gt;" -> ["string", "Map&lt;string, Customer&gt;"]
    /// </summary>
    /// <param name="typeParameters">The content inside the generic brackets</param>
    /// <returns>List of individual type parameter strings</returns>
    private List<string> ParseGenericTypeParameters(string typeParameters)
    {
        var parameters = new List<string>();
        var current = "";
        var depth = 0;

        for (int i = 0; i < typeParameters.Length; i++)
        {
            char c = typeParameters[i];

            if (c == '<')
            {
                depth++;
                current += c;
            }
            else if (c == '>')
            {
                depth--;
                current += c;
            }
            else if (c == ',' && depth == 0)
            {
                // Only split on commas at the top level (not inside nested generics)
                parameters.Add(current.Trim());
                current = "";
            }
            else
            {
                current += c;
            }
        }

        // Add the last parameter
        if (!string.IsNullOrWhiteSpace(current))
        {
            parameters.Add(current.Trim());
        }

        return parameters;
    }

    #endregion

    #region Error Handling and Recovery

    /// <summary>
    /// Adds an error to the error collection with current token position.
    /// </summary>
    /// <param name="message">Error description</param>
    private void AddError(string message)
    {
        var token = Peek();
        _errors.Add(new ParseError(_filePath, token.Line, token.Column, message));
    }

    /// <summary>
    /// Attempts to recover from a parsing error by skipping to the next statement.
    /// Looks for common statement terminators like ';', '}', or top-level keywords.
    /// </summary>
    private void SkipToNextStatement()
    {
        while (!IsAtEnd())
        {
            var token = Peek();

            // Stop at statement terminators
            if (token.Type == TokenType.Semicolon)
            {
                Advance(); // consume the semicolon
                break;
            }

            // Stop at block terminators  
            if (token.Type == TokenType.RightBrace || token.Type == TokenType.RightParen)
            {
                break;
            }

            // Stop at top-level keywords
            if (token.Type == TokenType.Import || token.Type == TokenType.Create ||
                (token.Type == TokenType.Identifier && token.Value == "rootPath"))
            {
                break;
            }

            Advance();
        }
    }

    /// <summary>
    /// Safe version of Consume that adds error instead of throwing.
    /// Returns null if the expected token is not found.
    /// </summary>
    /// <param name="type">Expected token type</param>
    /// <param name="message">Error message if expectation fails</param>
    /// <returns>The consumed token or null if not found</returns>
    private Token? SafeConsume(TokenType type, string message)
    {
        if (Check(type))
            return Advance();

        var current = Peek();
        AddError($"{message}. Got {current.Type} {ParseTokenValue(current)}");
        return null;
    }

    /// <summary>
    /// Parses the value of a token for special characters like newlines.
    /// </summary>
    /// <param name="current">The token to parse</param>
    /// <returns>The parsed value of the token</returns>
    private string ParseTokenValue(Token current)
    {
        return current.Type switch
        {
            TokenType.Newline => "",
            _ => $"'{current.Value}'"
        };
    }

    /// <summary>
    /// Smart recovery for malformed create statements.
    /// Skips tokens until it finds the end of the create block.
    /// Handles cases like:
    /// 1. create MachineStatus<path>(...) - missing enum/schema keyword
    /// 2. create enum Name<path> - missing parentheses
    /// 3. create schema Name<path - incomplete definitions
    /// </summary>
    private void SkipToEndOfCreate()
    {
        int parenDepth = 0;
        int angleDepth = 0;
        bool foundParens = false;

        while (!IsAtEnd())
        {
            var token = Peek();

            switch (token.Type)
            {
                case TokenType.LeftAngleBracket:
                    angleDepth++;
                    Advance();
                    break;

                case TokenType.RightAngleBracket:
                    angleDepth--;
                    Advance();
                    break;

                case TokenType.LeftParen:
                    foundParens = true;
                    parenDepth++;
                    Advance();
                    break;

                case TokenType.RightParen:
                    if (foundParens && parenDepth > 0)
                    {
                        parenDepth--;
                        Advance();

                        // If we've closed all parentheses, look for semicolon
                        if (parenDepth == 0)
                        {
                            if (Check(TokenType.Semicolon))
                            {
                                Advance(); // consume semicolon and exit
                                return;
                            }
                            // Even without semicolon, this might be the end
                            return;
                        }
                    }
                    else
                    {
                        // Orphaned closing paren - likely end of malformed create
                        Advance();
                        if (Check(TokenType.Semicolon))
                        {
                            Advance();
                            return;
                        }
                        return;
                    }
                    break;

                case TokenType.Semicolon:
                    // If we're not inside parentheses and angles, this ends the create
                    if (parenDepth == 0 && angleDepth == 0)
                    {
                        Advance();
                        return;
                    }
                    Advance();
                    break;

                case TokenType.Create:
                case TokenType.Import:
                    // Stop at next top-level construct
                    return;

                case TokenType.Identifier:
                    // Check if this looks like a rootPath or variable assignment
                    if (token.Value == "rootPath" || PeekNext()?.Type == TokenType.Equals)
                    {
                        return;
                    }
                    Advance();
                    break;

                default:
                    Advance();
                    break;
            }
        }
    }

    /// <summary>
    /// Smart recovery for orphaned property parsing failures.
    /// Skips tokens until it finds the end of the orphaned property statement.
    /// Handles orphaned properties like "Email: string" that should be inside schemas.
    /// </summary>
    private void SkipToEndOfOrphanedProperty()
    {
        while (!IsAtEnd())
        {
            var token = Peek();

            if (token.Type == TokenType.Semicolon)
            {
                Advance(); // consume semicolon and exit
                return;
            }

            // Stop at next top-level constructs
            if (token.Type == TokenType.Import || token.Type == TokenType.Create ||
                (token.Type == TokenType.Identifier && (token.Value == "rootPath" || PeekNext()?.Type == TokenType.Equals)))
            {
                return;
            }

            Advance();
        }
    }

    #endregion

    #region Import Parsing

    /// <summary>
    /// Parses an import statement.
    /// Grammar: 'import' '{' IdentifierList '}' 'from' StringLiteral ';'
    /// Example: import { CreateCustomerRequest, UpdateCustomerRequest } from "./customers.tgs";
    /// </summary>
    /// <returns>Import object with path and list of imported names, or null if parsing failed</returns>
    private Import? ParseImport()
    {
        if (SafeConsume(TokenType.Import, "Expected 'import'") == null) return null;

        if (SafeConsume(TokenType.LeftBrace, "Expected '{' after 'import'") == null)
        {
            SkipToEndOfImport();
            return null;
        }

        var importNames = new List<string>();

        // Parse comma-separated list of identifiers with better error recovery
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Check(TokenType.Comma))
            {
                Advance(); // consume comma
                continue;
            }

            var nameToken = SafeConsume(TokenType.Identifier, "Expected identifier in import list");
            if (nameToken != null)
            {
                importNames.Add(nameToken.Value);
            }
            else
            {
                // If we can't parse an identifier, skip to end of import
                SkipToEndOfImport();
                return null;
            }

            // Check what comes after the identifier
            if (Check(TokenType.Comma))
            {
                // Comma is good, continue
                continue;
            }
            else if (Check(TokenType.RightBrace))
            {
                // End of list, exit loop
                break;
            }
            else if (Check(TokenType.Identifier))
            {
                // Another identifier without comma - this is the error
                var nextToken = Peek();
                AddError($"Missing comma between import names '{nameToken.Value}' and '{nextToken.Value}'");
                SkipToEndOfImport();
                return null;
            }
            else
            {
                // Some other unexpected token
                AddError("Expected ',' or '}' after import name");
                SkipToEndOfImport();
                return null;
            }
        }

        if (SafeConsume(TokenType.RightBrace, "Expected '}' after import list") == null)
        {
            SkipToEndOfImport();
            return null;
        }

        if (SafeConsume(TokenType.From, "Expected 'from' after import list") == null)
        {
            SkipToEndOfImport();
            return null;
        }

        // Parse the file path (string literal)
        string? importPath = null;
        if (Check(TokenType.String))
        {
            importPath = Advance().Value;
        }
        else
        {
            AddError("Expected string literal for import path");
            SkipToEndOfImport();
            return null;
        }

        SafeConsume(TokenType.Semicolon, "Expected ';' after import statement");

        return new Import
        {
            Path = importPath,
            ImportNames = importNames
        };
    }

    /// <summary>
    /// Smart recovery for import parsing failures.
    /// Skips tokens until it finds the end of the import statement (semicolon).
    /// This prevents malformed import parts from being misinterpreted as other constructs.
    /// </summary>
    private void SkipToEndOfImport()
    {
        while (!IsAtEnd())
        {
            var token = Peek();

            if (token.Type == TokenType.Semicolon)
            {
                Advance(); // consume semicolon and exit
                return;
            }

            // Stop at next top-level constructs
            if (token.Type == TokenType.Import || token.Type == TokenType.Create ||
                (token.Type == TokenType.Identifier && token.Value == "rootPath"))
            {
                return;
            }

            Advance();
        }
    }

    #endregion

    #region Path and Variable Parsing

    /// <summary>
    /// Parses the special rootPath declaration.
    /// Grammar: 'rootPath' '=' Path ';'
    /// Example: rootPath = /Customers;
    /// </summary>
    /// <returns>The root path string, or null if parsing failed</returns>
    private string? ParseRootPath()
    {
        if (SafeConsume(TokenType.Identifier, "Expected 'rootPath'") == null) return null;
        if (SafeConsume(TokenType.Equals, "Expected '=' after 'rootPath'") == null) return null;

        string? path = null;
        if (Check(TokenType.Path))
        {
            path = Advance().Value;
        }
        else if (Check(TokenType.Slash))
        {
            path = ParsePathFromSlash();
        }
        else if (Check(TokenType.Identifier))
        {
            // Handle common mistake: rootPath = Machines; instead of rootPath = /Machines;
            var identifier = Advance().Value;
            AddError($"Root path must start with '/'. Did you mean '/'{identifier}' instead of '{identifier}'?");
            SkipToEndOfRootPath();
            return null;
        }
        else
        {
            AddError("Expected path after 'rootPath ='. Paths must start with '/'");
            SkipToEndOfRootPath();
            return null;
        }

        SafeConsume(TokenType.Semicolon, "Expected ';' after rootPath assignment");

        return "/" + path?.Trim('/');
    }

    /// <summary>
    /// Smart recovery for rootPath parsing failures.
    /// Skips tokens until it finds the end of the rootPath assignment (semicolon).
    /// This prevents malformed rootPath from being misinterpreted as variable assignments.
    /// </summary>
    private void SkipToEndOfRootPath()
    {
        while (!IsAtEnd())
        {
            var token = Peek();

            if (token.Type == TokenType.Semicolon)
            {
                Advance(); // consume semicolon and exit
                return;
            }

            // Stop at next top-level constructs
            if (token.Type == TokenType.Import || token.Type == TokenType.Create ||
                (token.Type == TokenType.Identifier && (token.Value == "rootPath" || PeekNext()?.Type == TokenType.Equals)))
            {
                return;
            }

            Advance();
        }
    }

    /// <summary>
    /// Parses a variable path assignment with support for concatenation.
    /// Grammar: Identifier '=' PathExpression ';'
    /// Examples: 
    ///   requestsDir = /Requests;
    ///   customDir = baseDir + /Custom;
    /// </summary>
    /// <param name="variablePaths">Previously defined variables for reference resolution</param>
    /// <returns>VariablePath object representing the assignment, or null if parsing failed</returns>
    private VariablePath? ParseVariableAssignment(List<VariablePath> variablePaths)
    {
        var nameToken = SafeConsume(TokenType.Identifier, "Expected variable name");
        if (nameToken == null) return null;

        if (SafeConsume(TokenType.Equals, "Expected '=' after variable name") == null) return null;

        var pathExpression = ParsePathExpression(variablePaths);
        if (pathExpression == null) return null;

        SafeConsume(TokenType.Semicolon, "Expected ';' after variable assignment");

        return new VariablePath(nameToken.Value, pathExpression);
    }

    /// <summary>
    /// Parses a path expression with support for concatenation using '+'.
    /// Grammar: PathPart ('+' PathPart)*
    /// Examples:
    ///   /Customers
    ///   baseDir + /Requests  
    ///   rootDir + customerDir + /Specific
    /// </summary>
    /// <param name="variablePaths">Available variables for resolution</param>
    /// <returns>Resolved path string, or null if parsing failed</returns>
    private string? ParsePathExpression(List<VariablePath> variablePaths)
    {
        var parts = new List<string>();

        // Parse the first part
        var firstPart = ParsePathPart(variablePaths);
        if (firstPart == null) return null;
        parts.Add(firstPart);

        // Handle + operations for path concatenation
        while (Check(TokenType.Plus))
        {
            Advance(); // consume the '+'
            var part = ParsePathPart(variablePaths);
            if (part != null) parts.Add(part);
        }

        // Resolve and combine all parts
        var resolvedParts = new List<string>();
        foreach (var part in parts)
        {
            resolvedParts.Add(part.Trim('/'));
        }

        return string.Join("/", resolvedParts);
    }

    /// <summary>
    /// Parses a single path part - either a literal path, variable reference, or path from slash.
    /// </summary>
    /// <param name="variablePaths">Available variables for resolution</param>
    /// <returns>Resolved path part, or null if parsing failed</returns>
    private string? ParsePathPart(List<VariablePath> variablePaths)
    {
        if (Check(TokenType.Path))
        {
            return Advance().Value;
        }
        else if (Check(TokenType.Slash))
        {
            return ParsePathFromSlash();
        }
        else if (Check(TokenType.Identifier))
        {
            // Variable reference - look up its value
            var varName = Advance().Value;
            var variable = variablePaths.FirstOrDefault(v => v.Name == varName);
            if (variable == null)
            {
                AddError($"Variable '{varName}' not found");
                return null;
            }
            return variable.Path;
        }
        else
        {
            AddError("Expected path or variable name");
            return null;
        }
    }

    /// <summary>
    /// Parses a path that starts with a slash but isn't tokenized as a single Path token.
    /// Handles cases like "/Customer/Requests" where it might be split across multiple tokens.
    /// </summary>
    /// <returns>Complete path string</returns>
    private string ParsePathFromSlash()
    {
        var pathBuilder = new List<string>();

        if (Check(TokenType.Slash))
            Advance();

        while (Check(TokenType.Identifier) || Check(TokenType.Path))
        {
            pathBuilder.Add(Advance().Value);
            if (Check(TokenType.Slash))
                Advance();
        }

        return string.Join("/", pathBuilder);
    }

    #endregion

    #region Schema and Enum Parsing

    /// <summary>
    /// Parses a schema definition.
    /// Grammar: 'schema' Identifier ('<' Path '>')? ('&' Identifier)? '(' PropertyList ')' ';'
    /// Examples:
    ///   create schema Customer<requestsDir>( Name: string; Email: string; );
    ///   create schema Employee<requestsDir> & Person ( EmployeeId: Uid; );
    /// </summary>
    /// <param name="rootPath">Root path for resolving relative paths</param>
    /// <param name="variablePaths">Available variables for path resolution</param>
    /// <returns>Schema object representing the definition, or null if parsing failed</returns>
    private Schema? ParseSchema(string? rootPath, List<VariablePath> variablePaths)
    {
        if (SafeConsume(TokenType.Schema, "Expected 'schema'") == null) return null;

        var nameToken = SafeConsume(TokenType.Identifier, "Expected schema name");
        if (nameToken == null)
        {
            SkipToEndOfSchema();
            return null;
        }

        // Optional output path specification
        string pathToOutput = "";
        if (Check(TokenType.LeftAngleBracket))
        {
            Advance(); // consume '<'
            var outputPath = ParseOutputPath(variablePaths);
            if (outputPath != null) pathToOutput = outputPath;
            SafeConsume(TokenType.RightAngleBracket, "Expected '>' after path");
        }

        // Optional inheritance with '&' syntax
        string? inheritanceName = null;
        if (Check(TokenType.Ampersand))
        {
            Advance(); // consume '&'
            var inheritanceToken = SafeConsume(TokenType.Identifier, "Expected inheritance name after '&'");
            if (inheritanceToken != null) inheritanceName = inheritanceToken.Value;
        }

        // Parse property list - with better error recovery
        if (SafeConsume(TokenType.LeftParen, "Expected '(' before schema properties") == null)
        {
            // If we can't find the opening parenthesis, skip the entire schema block
            SkipToEndOfSchema();
            return null;
        }

        var properties = ParseProperties();
        SafeConsume(TokenType.RightParen, "Expected ')' after schema properties");
        SafeConsume(TokenType.Semicolon, "Expected ';' after schema definition");

        // Combine root path with relative path
        var finalOutputPath = rootPath != null
            ? $"{rootPath.TrimEnd('/')}/{pathToOutput.TrimStart('/')}"
            : pathToOutput.TrimStart('/');

        return new Schema(finalOutputPath, nameToken.Value, properties, inheritanceName: inheritanceName);
    }

    /// <summary>
    /// Smart recovery for schema parsing failures.
    /// Skips tokens until it finds the end of the schema block (closing parenthesis and semicolon).
    /// This prevents schema properties from being misinterpreted as variable assignments.
    /// Handles various malformed schema patterns:
    /// 1. Missing opening parenthesis: create schema Name<path> properties... );
    /// 2. Missing closing parenthesis: create schema Name<path>( properties...
    /// 3. Empty schema: create schema Name<path>();
    /// </summary>
    private void SkipToEndOfSchema()
    {
        int parenDepth = 0;
        bool foundOpenParen = false;
        bool foundSemicolon = false;

        while (!IsAtEnd())
        {
            var token = Peek();

            switch (token.Type)
            {
                case TokenType.LeftParen:
                    foundOpenParen = true;
                    parenDepth++;
                    Advance();
                    break;

                case TokenType.RightParen:
                    if (foundOpenParen)
                    {
                        parenDepth--;
                        Advance();

                        // If we've closed all parentheses, look for semicolon
                        if (parenDepth == 0)
                        {
                            if (Check(TokenType.Semicolon))
                            {
                                Advance(); // consume semicolon and exit
                                return;
                            }
                        }
                    }
                    else
                    {
                        // Found closing paren without opening - likely orphaned end of schema properties
                        Advance();
                        if (Check(TokenType.Semicolon))
                        {
                            Advance();
                            return;
                        }
                        // Even without semicolon, this might be the end
                        return;
                    }
                    break;

                case TokenType.Semicolon:
                    if (!foundOpenParen || parenDepth == 0)
                    {
                        foundSemicolon = true;
                        Advance();

                        // If we found a semicolon without opening paren, we need to be more careful
                        // There might be orphaned schema properties following
                        if (!foundOpenParen)
                        {
                            // Continue skipping until we find clear end markers
                            continue;
                        }
                        return;
                    }
                    Advance();
                    break;

                case TokenType.Create:
                case TokenType.Import:
                    // Stop at next top-level construct
                    return;

                case TokenType.Identifier:
                    // If we've already seen a semicolon and this identifier is followed by =, 
                    // it's likely a variable assignment (next top-level construct)
                    if (foundSemicolon && !foundOpenParen && PeekNext()?.Type == TokenType.Equals)
                    {
                        return;
                    }

                    // Check if this looks like a rootPath assignment
                    if (token.Value == "rootPath")
                    {
                        return;
                    }

                    Advance();
                    break;

                default:
                    Advance();
                    break;
            }
        }
    }

    /// <summary>
    /// Parses an enum definition.
    /// Grammar: 'enum' Identifier ('<' Path '>')? '(' ValueList ')' ';'
    /// Example: create enum CustomerStatus<enumsDir>( Active, Inactive, Pending );
    /// </summary>
    /// <param name="rootPath">Root path for resolving relative paths</param>
    /// <param name="variablePaths">Available variables for path resolution</param>
    /// <returns>TgEnum object representing the definition, or null if parsing failed</returns>
    private TgEnum? ParseEnum(string? rootPath, List<VariablePath> variablePaths)
    {
        if (SafeConsume(TokenType.Enum, "Expected 'enum'") == null) return null;

        var nameToken = SafeConsume(TokenType.Identifier, "Expected enum name");
        if (nameToken == null)
        {
            SkipToEndOfEnum();
            return null;
        }

        // Optional output path specification
        string pathToOutput = "";
        if (Check(TokenType.LeftAngleBracket))
        {
            Advance(); // consume '<'
            var outputPath = ParseOutputPath(variablePaths);
            if (outputPath != null) pathToOutput = outputPath;
            SafeConsume(TokenType.RightAngleBracket, "Expected '>' after path");
        }

        // Parse enum values - with better error recovery
        if (SafeConsume(TokenType.LeftParen, "Expected '(' before enum values") == null)
        {
            // If we can't find the opening parenthesis, skip the entire enum block
            SkipToEndOfEnum();
            return null;
        }

        var values = ParseEnumValues();
        SafeConsume(TokenType.RightParen, "Expected ')' after enum values");
        SafeConsume(TokenType.Semicolon, "Expected ';' after enum definition");

        // Combine root path with relative path
        var finalOutputPath = rootPath != null
            ? $"{rootPath.TrimEnd('/')}/{pathToOutput.TrimStart('/')}"
            : pathToOutput.TrimStart('/');

        return new TgEnum(nameToken.Value, values, finalOutputPath);
    }

    /// <summary>
    /// Smart recovery for enum parsing failures.
    /// Skips tokens until it finds the end of the enum block (closing parenthesis and semicolon).
    /// This prevents enum values from being misinterpreted as variable assignments.
    /// Handles various malformed enum patterns:
    /// 1. Missing opening parenthesis: create enum Name<path>; values... );
    /// 2. Missing closing parenthesis: create enum Name<path>( values...
    /// 3. Empty enum: create enum Name<path>();
    /// </summary>
    private void SkipToEndOfEnum()
    {
        int parenDepth = 0;
        bool foundOpenParen = false;
        bool foundSemicolon = false;

        while (!IsAtEnd())
        {
            var token = Peek();

            switch (token.Type)
            {
                case TokenType.LeftParen:
                    foundOpenParen = true;
                    parenDepth++;
                    Advance();
                    break;

                case TokenType.RightParen:
                    if (foundOpenParen)
                    {
                        parenDepth--;
                        Advance();

                        // If we've closed all parentheses, look for semicolon
                        if (parenDepth == 0)
                        {
                            if (Check(TokenType.Semicolon))
                            {
                                Advance(); // consume semicolon and exit
                                return;
                            }
                        }
                    }
                    else
                    {
                        // Found closing paren without opening - likely orphaned end of enum values
                        Advance();
                        if (Check(TokenType.Semicolon))
                        {
                            Advance();
                            return;
                        }
                        // Even without semicolon, this might be the end
                        return;
                    }
                    break;

                case TokenType.Semicolon:
                    if (!foundOpenParen || parenDepth == 0)
                    {
                        foundSemicolon = true;
                        Advance();

                        // If we found a semicolon without opening paren, we need to be more careful
                        // There might be orphaned enum values following
                        if (!foundOpenParen)
                        {
                            // Continue skipping until we find clear end markers
                            continue;
                        }
                        return;
                    }
                    Advance();
                    break;

                case TokenType.Create:
                case TokenType.Import:
                    // Stop at next top-level construct
                    return;

                case TokenType.Identifier:
                    // If we've already seen a semicolon and this identifier is followed by =, 
                    // it's likely a variable assignment (next top-level construct)
                    if (foundSemicolon && !foundOpenParen && PeekNext()?.Type == TokenType.Equals)
                    {
                        return;
                    }

                    // Check if this looks like a rootPath assignment
                    if (token.Value == "rootPath")
                    {
                        return;
                    }

                    Advance();
                    break;

                default:
                    Advance();
                    break;
            }
        }
    }

    /// <summary>
    /// Parses the output path specification within angle brackets.
    /// Can be a literal path, variable reference, or path starting with slash.
    /// </summary>
    /// <param name="variablePaths">Available variables for resolution</param>
    /// <returns>Resolved output path, or null if parsing failed</returns>
    private string? ParseOutputPath(List<VariablePath> variablePaths)
    {
        if (Check(TokenType.Path))
        {
            return Advance().Value.TrimStart('/');
        }
        else if (Check(TokenType.Slash))
        {
            return ParsePathFromSlash();
        }
        else if (Check(TokenType.Identifier))
        {
            var varName = Advance().Value;
            var variable = variablePaths.FirstOrDefault(v => v.Name == varName);
            if (variable == null)
            {
                AddError($"Variable '{varName}' not found");
                return null;
            }
            return variable.Path;
        }
        else
        {
            AddError("Expected path or variable name");
            return null;
        }
    }

    #endregion

    #region Property and Type Parsing

    /// <summary>
    /// Parses a list of schema properties.
    /// Grammar: (Property)*
    /// Property: Identifier ':' Type ('?')? ';'
    /// Examples:
    ///   Name: string;
    ///   Email: string?;
    ///   Items: Array<string>;
    /// </summary>
    /// <returns>List of property definitions</returns>
    private List<TgProperty> ParseProperties()
    {
        var properties = new List<TgProperty>();

        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            // Skip newlines inside property list
            if (Check(TokenType.Newline))
            {
                Advance();
                continue;
            }

            var propNameToken = SafeConsume(TokenType.Identifier, "Expected property name");
            if (propNameToken == null)
            {
                // Try to recover by skipping to next property or end
                SkipToNextPropertyOrEnd();
                continue;
            }

            if (SafeConsume(TokenType.Colon, "Expected ':' after property name") == null)
            {
                SkipToNextPropertyOrEnd();
                continue;
            }

            var typeNameToken = SafeConsume(TokenType.Identifier, "Expected type name");
            if (typeNameToken == null)
            {
                SkipToNextPropertyOrEnd();
                continue;
            }

            var typeName = typeNameToken.Value;

            // Handle generic types like Array<string>, Map<string, number>
            if (Check(TokenType.LeftAngleBracket))
            {
                var genericPart = ParseGenericType();
                if (genericPart != null) typeName += genericPart;
            }

            // Handle nullable types with '?'
            var isNullable = false;
            if (Check(TokenType.QuestionMark))
            {
                isNullable = true;
                Advance();
            }

            if (SafeConsume(TokenType.Semicolon, "Expected ';' after property") != null)
            {
                properties.Add(new TgProperty(typeName, propNameToken.Value, isNullable, typeNameToken.Line, typeNameToken.Column));
            }
        }

        return properties;
    }

    /// <summary>
    /// Skips to the next property or end of property list during error recovery.
    /// </summary>
    private void SkipToNextPropertyOrEnd()
    {
        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            if (Check(TokenType.Semicolon))
            {
                Advance(); // consume semicolon and stop
                break;
            }
            if (Check(TokenType.Identifier) && PeekNext()?.Type == TokenType.Colon)
            {
                // Looks like the start of next property
                break;
            }
            Advance();
        }
    }

    /// <summary>
    /// Parses generic type parameters.
    /// Grammar: '<' TypeList '>'
    /// Examples: <string>, <string, number>, <Array<Customer>>
    /// Handles nested generics correctly by tracking bracket depth.
    /// </summary>
    /// <returns>Complete generic type string including brackets, or null if parsing failed</returns>
    private string? ParseGenericType()
    {
        var result = "";
        if (SafeConsume(TokenType.LeftAngleBracket, "Expected '<'") == null) return null;
        result += "<";

        var depth = 1;
        while (depth > 0 && !IsAtEnd())
        {
            var token = Advance();

            if (token.Type == TokenType.LeftAngleBracket)
                depth++;
            else if (token.Type == TokenType.RightAngleBracket)
                depth--;

            result += token.Value;

            // Add spacing after commas for readability
            if (depth > 0 && (Check(TokenType.Identifier) || Check(TokenType.Comma)))
            {
                result += token.Value == "," ? ", " : "";
            }
        }

        return result;
    }

    /// <summary>
    /// Parses enum values with strict comma handling.
    /// Grammar: Identifier (',' Identifier)* ','?
    /// Example: Active, Inactive, Pending
    /// 
    /// This method enforces proper comma separation and provides clear error messages
    /// when commas are missing between enum values.
    /// Also validates that enums are not empty.
    /// </summary>
    /// <returns>List of enum value names</returns>
    private List<string> ParseEnumValues()
    {
        var values = new List<string>();

        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            // Skip newlines inside enum values
            if (Check(TokenType.Newline))
            {
                Advance();
                continue;
            }

            var valueToken = SafeConsume(TokenType.Identifier, "Expected enum value");
            if (valueToken == null)
            {
                // Try to recover
                SkipToNextEnumValueOrEnd();
                continue;
            }

            values.Add(valueToken.Value);

            // Skip any newlines after the enum value
            while (Check(TokenType.Newline))
            {
                Advance();
            }

            if (Check(TokenType.Comma))
            {
                Advance();
                // Skip newlines after comma
                while (Check(TokenType.Newline))
                {
                    Advance();
                }
            }
            else if (!Check(TokenType.RightParen))
            {
                // If we're not at the end and there's no comma, it's an error
                var nextToken = Peek();
                if (nextToken.Type == TokenType.Identifier)
                {
                    AddError($"Missing comma between enum values '{valueToken.Value}' and '{nextToken.Value}'");
                }
                else
                {
                    AddError("Expected ',' or ')' after enum value");
                }
                // Try to continue anyway
            }
        }

        // Validate that enum is not empty
        if (values.Count == 0)
        {
            AddError("Enum cannot be empty. Please define at least one enum value");
        }

        return values;
    }

    /// <summary>
    /// Skips to the next enum value or end of enum list during error recovery.
    /// </summary>
    private void SkipToNextEnumValueOrEnd()
    {
        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            if (Check(TokenType.Comma))
            {
                Advance(); // consume comma and stop
                break;
            }
            if (Check(TokenType.Identifier))
            {
                // Looks like next enum value
                break;
            }
            Advance();
        }
    }

    #endregion

    #region Parser Utilities

    /// <summary>
    /// Consumes a token of the expected type or throws an error.
    /// This is the main way to enforce grammar rules.
    /// </summary>
    /// <param name="type">Expected token type</param>
    /// <param name="message">Error message if expectation fails</param>
    /// <returns>The consumed token</returns>
    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
            return Advance();

        var current = Peek();
        throw new Exception($"{message}. Got {current.Type} {ParseTokenValue(current)} at line {current.Line}, column {current.Column}");
    }

    /// <summary>
    /// Checks if the current token is of the specified type without consuming it.
    /// </summary>
    /// <param name="type">Token type to check</param>
    /// <returns>True if current token matches the type</returns>
    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }

    /// <summary>
    /// Checks if the current token is an identifier with a specific value.
    /// Used for context-sensitive keywords like "rootPath".
    /// </summary>
    /// <param name="value">Expected identifier value</param>
    /// <returns>True if current token is identifier with matching value</returns>
    private bool CheckIdentifier(string value)
    {
        if (IsAtEnd()) return false;
        var token = Peek();
        return token.Type == TokenType.Identifier && token.Value == value;
    }

    /// <summary>
    /// Consumes and returns the current token, advancing to the next one.
    /// </summary>
    /// <returns>The token that was consumed</returns>
    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    /// <summary>
    /// Checks if we've reached the end of the token stream.
    /// </summary>
    /// <returns>True if at EOF token</returns>
    private bool IsAtEnd()
    {
        return Peek().Type == TokenType.EOF;
    }

    /// <summary>
    /// Returns the current token without consuming it.
    /// </summary>
    /// <returns>Current token</returns>
    private Token Peek()
    {
        return _tokens[_current];
    }

    /// <summary>
    /// Returns the next token without consuming it.
    /// </summary>
    /// <returns>Next token, or null if at end</returns>
    private Token? PeekNext()
    {
        if (_current + 1 >= _tokens.Count) return null;
        return _tokens[_current + 1];
    }

    /// <summary>
    /// Returns the previously consumed token.
    /// </summary>
    /// <returns>Previous token</returns>
    private Token Previous()
    {
        return _tokens[_current - 1];
    }

    #endregion
}