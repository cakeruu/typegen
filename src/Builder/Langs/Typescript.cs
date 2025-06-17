using System.Text;
using System.Text.RegularExpressions;
using typegen.Builder.Types;

namespace typegen.Builder.Langs;

/// <summary>
/// Represents a TypeScript type mapping for a TGS type.
/// Contains the equivalent TypeScript type representation.
/// </summary>
public record TypescriptType(
    string TypescriptTypeTranslation
);

/// <summary>
/// TypeScript language transpiler for the TypeGen system.
/// 
/// This class converts TGS (TypeGen Schema) definitions into TypeScript type definitions.
/// It handles:
/// - Type mapping from TGS types to TypeScript types
/// - Generate proper TypeScript interfaces and type aliases
/// - Creating relative import statements between generated files
/// - File naming conventions (kebab-case with .type.ts extension)
/// - Directory structure based on schema path specifications
/// - Generic type support (Array&lt;T&gt;, Map&lt;K,V&gt;, etc.)
/// - Nullable type support with optional properties
/// - Schema inheritance using intersection types (&amp;)
/// 
/// Generated files follow TypeScript best practices:
/// - Named exports for all types
/// - Proper relative imports
/// - Clear interface definitions
/// - Optional property syntax for nullable fields
/// </summary>
public class Typescript : Lang<TypescriptType>
{
    /// <summary>
    /// Maps TGS primitive types to their TypeScript equivalents.
    /// This is the foundation of type translation between the two systems.
    /// </summary>
    /// <returns>Dictionary mapping TGS type names to TypeScript types</returns>
    protected override Dictionary<string, TypescriptType> SetTypeTranslations()
    {
        return new Dictionary<string, TypescriptType>
        {
            // Identifier types
            { TgType.Uid, new TypescriptType("string") },
            
            // Basic types
            { TgType.String, new TypescriptType("string") },
            { TgType.Bool, new TypescriptType("boolean") },
            { TgType.Char, new TypescriptType("string") },
            
            // Numeric types (all map to 'number' in TypeScript)
            { TgType.Int, new TypescriptType("number") },
            { TgType.UInt, new TypescriptType("number") },
            { TgType.Long, new TypescriptType("number") },
            { TgType.ULong, new TypescriptType("number") },
            { TgType.Short, new TypescriptType("number") },
            { TgType.UShort, new TypescriptType("number") },
            { TgType.Byte, new TypescriptType("number") },
            { TgType.SByte, new TypescriptType("number") },
            { TgType.Float, new TypescriptType("number") },
            { TgType.Double, new TypescriptType("number") },
            { TgType.Decimal, new TypescriptType("number") },
            
            // Complex types
            { TgType.Object, new TypescriptType("any") },
            { TgType.Array, new TypescriptType("[]") },
            { TgType.List, new TypescriptType("Array") },
            { TgType.Map, new TypescriptType("Map") },
            { TgType.Set, new TypescriptType("Set") },
            { TgType.Queue, new TypescriptType("Array") },
            
            // Date/time types
            { TgType.Date, new TypescriptType("Date") },
            { TgType.DateTime, new TypescriptType("Date") },
        };
    }

    // State maintained during transpilation
    private string _firstDirective = "export type";           // TypeScript export syntax
    private Dictionary<string, string> _normalizedSchemaNames = [];  // Schema name to file name mapping
    private List<Schema> _allSchemas = [];                    // All schemas for cross-reference
    private List<TgEnum> _allEnums = [];                      // All enums for cross-reference
    private string _outputPath = "";                          // Base output directory
    private string? _rootPath = null;                         // Root path from schema file

    /// <summary>
    /// Main transpilation entry point. Processes all schema files and generates TypeScript code.
    /// 
    /// The process:
    /// 1. Collect all schemas and enums for cross-referencing
    /// 2. For each schema file:
    ///    a. Setup type mappings for schemas and enums
    ///    b. Generate individual .type.ts files for each schema/enum
    ///    c. Handle import statements between files
    ///    d. Create proper directory structure
    /// 3. Return list of generated files for writing to disk
    /// </summary>
    /// <param name="schemaFiles">Parsed schema files to transpile</param>
    /// <param name="outputPath">Base output directory path</param>
    /// <returns>List of generated TypeScript files</returns>
    public override List<TranspiledFile> TranspileFiles(List<SchemaFile> schemaFiles, string outputPath)
    {
        var transpiledFiles = new List<TranspiledFile>();
        _outputPath = outputPath;

        // Collect all schemas and enums for cross-file references
        _allSchemas = schemaFiles.SelectMany(sf => sf.Schemas).ToList();
        _allEnums = schemaFiles.SelectMany(sf => sf.Enums).ToList();

        // Process each schema file independently
        schemaFiles.ForEach(schemaFile =>
        {
            _rootPath = schemaFile.RootPath;

            // Create normalized name mappings for this file
            _normalizedSchemaNames = schemaFile.Schemas.Select(schema => (schema.Name, FormatSchemaOrEnumName(schema.Name)))
                .ToDictionary();

            // Add imported schema names to mapping
            var importsSchemas = schemaFile.Imports.SelectMany(i => i.Schemas).ToList();
            var importsEnums = schemaFile.Imports.SelectMany(i => i.Enums).ToList();

            foreach (var import in importsSchemas)
            {
                _normalizedSchemaNames[import.Name] = FormatSchemaOrEnumName(import.Name);
            }

            foreach (var import in importsEnums)
            {
                _normalizedSchemaNames[import.Name] = FormatSchemaOrEnumName(import.Name);
            }

            foreach (var e in schemaFile.Enums)
            {
                _normalizedSchemaNames[e.Name] = FormatSchemaOrEnumName(e.Name);
            }

            // Add schemas and enums to type translation system temporarily
            AddSchemasToTypeTranslations(schemaFile.Schemas, importsSchemas);
            AddEnumsToTypeTranslations(schemaFile.Enums, importsEnums);

            // Generate TypeScript files for each schema
            schemaFile.Schemas.ForEach(schema =>
            {
                var schemaOutputPath = GetSchemaOutputPath(schema);
                var path = $"{outputPath}/{schemaOutputPath}";
                var content = new StringBuilder();

                // Generate the type definition
                AppendStartOfTheType(content, schema);
                AddProps(content, schema);
                content.AppendLine("}");

                var pathWithFile = $"{path}{_normalizedSchemaNames[schema.Name]}.type.ts";
                transpiledFiles.Add(new TranspiledFile(pathWithFile, content.ToString()));
            });

            // Clean up temporary type mappings
            RemoveSchemasFromTypeTranslations(schemaFile.Schemas, importsSchemas);
            RemoveEnumsFromTypeTranslations(schemaFile.Enums, importsEnums);

            // Generate TypeScript files for each enum
            schemaFile.Enums.ForEach(e =>
            {
                var enumOutputPath = GetEnumOutputPath(e);
                var path = $"{outputPath}/{enumOutputPath}";
                var content = new StringBuilder();

                AppendEnum(content, e);

                var pathWithFile = $"{path}{_normalizedSchemaNames[e.Name]}.type.ts";
                transpiledFiles.Add(new TranspiledFile(pathWithFile, content.ToString()));
            });

            _normalizedSchemaNames = [];
        });

        return transpiledFiles;
    }

    #region Enum Generation

    /// <summary>
    /// Generates TypeScript union type definition for an enum.
    /// 
    /// Example output:
    /// export enum CustomerStatus {
    ///     Active = "Active",
    ///     Inactive = "Inactive",
    ///     Pending = "Pending"
    /// }
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="e">Enum definition to generate</param>
    private void AppendEnum(StringBuilder content, TgEnum e)
    {
        content.Append($"export enum {e.Name} {{\n");
        for (int i = 0; i < e.Values.Count; i++)
        {
            content.Append($"  {e.Values[i]} = {i}");
            if (i < e.Values.Count - 1)
            {
                content.Append(",\n");
            }
        }
        content.AppendLine("\n}");
    }

    #endregion

    #region Type Translation Management

    /// <summary>
    /// Temporarily adds schemas to the type translation system.
    /// This allows schemas to reference each other during generation.
    /// </summary>
    private void AddSchemasToTypeTranslations(List<Schema> schemaFileSchemas, List<Schema> imports)
    {
        schemaFileSchemas.ForEach(AddSchemaToTypeTranslations);
        imports.ForEach(AddSchemaToTypeTranslations);
    }

    /// <summary>
    /// Temporarily adds enums to the type translation system.
    /// </summary>
    private void AddEnumsToTypeTranslations(List<TgEnum> schemaFileEnums, List<TgEnum> importsEnums)
    {
        schemaFileEnums.ForEach(AddEnumToTypeTranslations);
        importsEnums.ForEach(AddEnumToTypeTranslations);
    }

    private void AddEnumToTypeTranslations(TgEnum tgEnum)
    {
        TypeTranslations.Add(tgEnum.Name, new TypescriptType(tgEnum.Name));
    }

    /// <summary>
    /// Removes schemas from type translation system after processing.
    /// This prevents namespace pollution between different schema files.
    /// </summary>
    private void RemoveSchemasFromTypeTranslations(List<Schema> schemaFileSchemas, List<Schema> imports)
    {
        schemaFileSchemas.ForEach(schema => TypeTranslations.Remove(schema.Name));
        imports.ForEach(schema => TypeTranslations.Remove(schema.Name));
    }

    private void RemoveEnumsFromTypeTranslations(List<TgEnum> schemaFileEnums, List<TgEnum> importsEnums)
    {
        schemaFileEnums.ForEach(tgEnum => TypeTranslations.Remove(tgEnum.Name));
        importsEnums.ForEach(tgEnum => TypeTranslations.Remove(tgEnum.Name));
    }

    private void AddSchemaToTypeTranslations(Schema schema)
    {
        TypeTranslations.Add(schema.Name, new TypescriptType(schema.Name));
    }

    #endregion

    #region File Naming

    /// <summary>
    /// Converts schema/enum names to kebab-case file names.
    /// 
    /// Examples:
    /// - CreateCustomerRequest -> create-customer-request
    /// - UserID -> user-id
    /// - XMLHttpRequest -> xml-http-request
    /// 
    /// This follows TypeScript/JavaScript naming conventions for file names.
    /// </summary>
    /// <param name="schemaName">Original schema/enum name</param>
    /// <returns>Kebab-case file name</returns>
    private string FormatSchemaOrEnumName(string schemaName)
    {
        // First, handle underscore-separated words
        string normalized = Regex.Replace(schemaName, @"_(\w)", m => m.Groups[1].Value.ToUpper());

        var formattedName = new StringBuilder();
        bool isPreviousCapital = false;
        bool isConsecutiveCapitals = false;

        for (int i = 0; i < normalized.Length; i++)
        {
            char currentChar = normalized[i];
            bool isCurrentCapital = char.IsUpper(currentChar);

            if (isCurrentCapital && isPreviousCapital)
            {
                isConsecutiveCapitals = true;
            }

            // Add hyphen before capital letters in certain conditions
            if (formattedName.Length > 0 &&
                ((isCurrentCapital && !isPreviousCapital) ||
                 (isConsecutiveCapitals && !isPreviousCapital) ||
                 (isCurrentCapital && isConsecutiveCapitals && !char.IsUpper(normalized[i - 1]))))
            {
                formattedName.Append('-');
            }

            formattedName.Append(char.ToLower(currentChar));

            isPreviousCapital = isCurrentCapital;

            if (!isCurrentCapital)
            {
                isConsecutiveCapitals = false;
            }
        }

        return formattedName.ToString();
    }

    #endregion

    #region Property Generation

    /// <summary>
    /// Generates TypeScript properties for a schema and handles import generation.
    /// 
    /// For each property:
    /// 1. Determines the TypeScript type
    /// 2. Handles complex/generic types
    /// 3. Generates import statements for referenced schemas/enums
    /// 4. Writes the property definition
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="schema">Schema containing the properties</param>
    private void AddProps(StringBuilder content, Schema schema)
    {
        schema.Props.ForEach(prop =>
        {
            var propType = prop.Type;

            // Handle different types of property types
            switch (propType)
            {
                case var _ when TypeTranslations.ContainsKey(propType):
                    // Simple type mapping
                    WriteProp(content, prop);
                    break;
                case var _ when propType.Contains('<'):
                    // Generic type like Array<string> or Map<string, number>
                    WriteComplexProp(content, prop);
                    break;
                default:
                    throw new TypeNotSupportedException(propType);
            }

            // Generate import statements for referenced schemas
            if (_allSchemas.FirstOrDefault(s => s.Name == propType) is { } schemaProp)
            {
                var parentPath = $"{_outputPath}/{GetSchemaOutputPath(schemaProp)}";
                var childPath = $"{_outputPath}/{GetSchemaOutputPath(schema)}";

                var import = $"import {{ {schemaProp.Name} }} from '{TransformPathToRelative($"{parentPath}{_normalizedSchemaNames[schemaProp.Name]}",
                        $"{childPath}{_normalizedSchemaNames[schema.Name]}")}.type';\n";

                // Avoid duplicate imports
                if (!content.ToString().Contains(import))
                {
                    content.Insert(0, import);
                }
            }

            // Generate import statements for referenced enums
            if (_allEnums.FirstOrDefault(e => e.Name == propType) is { } enumProp)
            {
                var parentPath = $"{_outputPath}/{GetEnumOutputPath(enumProp)}";
                var childPath = $"{_outputPath}/{GetSchemaOutputPath(schema)}";

                var import = $"import {{ {enumProp.Name} }} from '{TransformPathToRelative($"{parentPath}{_normalizedSchemaNames[enumProp.Name]}",
                        $"{childPath}{_normalizedSchemaNames[schema.Name]}")}.type';\n";

                if (!content.ToString().Contains(import))
                {
                    content.Insert(0, import);
                }
            }
        });
    }

    /// <summary>
    /// Generates the opening of a TypeScript interface with optional inheritance.
    /// 
    /// Examples:
    /// - export type Customer = {
    /// - export type Employee = Person & {
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="schema">Schema definition</param>
    private void AppendStartOfTheType(StringBuilder content, Schema schema)
    {
        string inheritance = "";
        if (schema.Inheritance is not null)
        {
            inheritance = $" {schema.Inheritance.Name} &";
            var parentPath = $"{_outputPath}/{GetSchemaOutputPath(schema.Inheritance)}";
            var childPath = $"{_outputPath}/{GetSchemaOutputPath(schema)}";

            // Add import for the parent type
            content.Insert(
                0,
                $"import {{ {schema.Inheritance.Name} }} from '{TransformPathToRelative($"{parentPath}{_normalizedSchemaNames[schema.Inheritance.Name]}",
                        $"{childPath}{_normalizedSchemaNames[schema.Name]}")}.type';\n"
            );
        }

        content.AppendLine($"{_firstDirective} {schema.Name} ={inheritance} {{");
    }

    /// <summary>
    /// Converts absolute paths to relative import paths for TypeScript.
    /// 
    /// Example:
    /// - From: /output/customers/customer.type.ts
    /// - To: /output/orders/order.type.ts
    /// - Result: ../customers/customer
    /// </summary>
    /// <param name="pathToImport">Absolute path of file to import</param>
    /// <param name="pathToExport">Absolute path of file doing the importing</param>
    /// <returns>Relative import path</returns>
    private static string TransformPathToRelative(string pathToImport, string pathToExport)
    {
        string[] importParts = pathToImport.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] exportParts = pathToExport.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        int commonPrefixLength = GetCommonPrefixLength(importParts, exportParts);

        int levelsUp = exportParts.Length - commonPrefixLength - 1;

        var relativePath = new List<string>();

        // Add "../" for each level up needed
        for (int i = 0; i < levelsUp; i++)
        {
            relativePath.Add("..");
        }

        // Add the remaining path components
        for (int i = commonPrefixLength; i < importParts.Length; i++)
        {
            relativePath.Add(importParts[i]);
        }

        string result = string.Join("/", relativePath);
        result = result.Replace(".type", ""); // Remove .type from import path

        // Ensure relative paths start with ./ or ../
        if (!result.StartsWith("./") && !result.StartsWith("../"))
        {
            result = $"./{result}";
        }
        return result;
    }

    /// <summary>
    /// Finds the common prefix length between two path arrays.
    /// Used for calculating relative paths.
    /// </summary>
    private static int GetCommonPrefixLength(string[] importParts, string[] exportParts)
    {
        int commonPrefixLength = 0;
        for (int i = 0; i < Math.Min(importParts.Length, exportParts.Length); i++)
        {
            if (importParts[i].Equals(exportParts[i], StringComparison.OrdinalIgnoreCase))
            {
                commonPrefixLength++;
            }
            else
            {
                break;
            }
        }
        return commonPrefixLength;
    }

    /// <summary>
    /// Gets the output path for a schema based on its PathToOutput property.
    /// </summary>
    private string GetSchemaOutputPath(Schema schema)
    {
        // Warning: Care with the trim start, it might be a problem
        return string.IsNullOrEmpty(schema.PathToOutput) ? "" : $"{schema.PathToOutput.TrimStart('/')}/";
    }

    /// <summary>
    /// Gets the output path for an enum based on its PathToOutput property.
    /// </summary>
    private string GetEnumOutputPath(TgEnum e)
    {
        // Warning: Care with the trim start, it might be a problem
        return string.IsNullOrEmpty(e.PathToOutput) ? "" : $"{e.PathToOutput.TrimStart('/')}/";
    }

    /// <summary>
    /// Writes a property with a complex/generic type like Array&lt;string&gt; or Map&lt;K,V&gt;.
    /// </summary>
    private void WriteComplexProp(StringBuilder content, TgProperty prop)
    {
        var typeScriptTypeTranslation = TranslateComplexTypeRec(prop.Type.Trim()).TypescriptTypeTranslation;
        AppendProp(content, prop, typeScriptTypeTranslation);
    }

    /// <summary>
    /// Recursively translates complex generic types to TypeScript.
    /// 
    /// Examples:
    /// - Array&lt;string&gt; -> string[]
    /// - Map&lt;string, number&gt; -> Map&lt;string, number&gt;
    /// - Array&lt;Map&lt;string, Customer&gt;&gt; -> Map&lt;string, Customer&gt;[]
    /// </summary>
    /// <param name="propType">TGS type to translate</param>
    /// <returns>TypeScript type representation</returns>
    private TypescriptType TranslateComplexTypeRec(string propType)
    {
        // Base case: no generics
        if (!propType.Contains('<'))
        {
            if (propType == "Array")
            {
                throw new ArgumentException("Array type must have a type parameter");
            }

            return TypeTranslations[propType];
        }

        // Parse generic type: OuterType<Param1, Param2, ...>
        var match = Regex.Match(propType, @"^(\w+)<(.+)>$");
        var typeParams = SplitGenericTypes(match.Groups[2].Value);

        var outerTypeName = match.Groups[1].Value;

        // Special handling for Array type - convert to TypeScript array syntax
        if (outerTypeName == "Array")
        {
            var innerType = TranslateComplexTypeRec(typeParams[0].Trim());
            return new TypescriptType(TypescriptTypeTranslation: $"{innerType.TypescriptTypeTranslation}[]");
        }

        // Generic type translation
        var csharpType = TypeTranslations[outerTypeName];

        var translatedParams = typeParams.Select(param => TranslateComplexTypeRec(param.Trim())).ToList();

        var typeTranslated =
            $"{csharpType.TypescriptTypeTranslation}<{string.Join(", ", translatedParams.Select(t => t.TypescriptTypeTranslation))}>";

        return new TypescriptType(typeTranslated);
    }

    /// <summary>
    /// Splits generic type parameters, respecting nested generics.
    /// 
    /// Example: "string, Map&lt;string, number&gt;, boolean"
    /// Returns: ["string", "Map&lt;string, number&gt;", "boolean"]
    /// </summary>
    /// <param name="typeContent">Content inside generic brackets</param>
    /// <returns>List of individual type parameters</returns>
    private static List<string> SplitGenericTypes(string typeContent)
    {
        var results = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeContent.Length; i++)
        {
            switch (typeContent[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
            }

            // Only split on commas at depth 0 (not inside nested generics)
            if (depth != 0 || typeContent[i] != ',') continue;

            results.Add(typeContent.Substring(start, i - start).Trim());
            start = i + 1;
        }

        results.Add(typeContent[start..].Trim());

        return results;
    }

    /// <summary>
    /// Writes a property with a simple type mapping.
    /// </summary>
    private void WriteProp(StringBuilder content, TgProperty prop)
    {
        var typeScriptTypeTranslation = TypeTranslations[prop.Type].TypescriptTypeTranslation;
        AppendProp(content, prop, typeScriptTypeTranslation);
    }

    /// <summary>
    /// Appends a property definition to the TypeScript interface.
    /// 
    /// Example output:
    /// - name: string;
    /// - email?: string;
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="prop">Property definition</param>
    /// <param name="typeScriptTypeTranslation">TypeScript type string</param>
    private void AppendProp(StringBuilder content, TgProperty prop, string typeScriptTypeTranslation)
    {
        var nullable = prop.IsNullable ? "?" : "";
        content.AppendLine($"  {prop.Name}{nullable}: {typeScriptTypeTranslation};");
    }

    #endregion
}