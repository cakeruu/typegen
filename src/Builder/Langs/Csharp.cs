using System.Text;
using System.Text.RegularExpressions;
using typegen.Builder.Types;

namespace typegen.Builder.Langs;

/// <summary>
/// Represents a C# type mapping for a TGS type.
/// Contains the equivalent C# type representation and optional using directive.
/// </summary>
public record CsharpType(
    string CsharpTypeTranslation,
    string? UsingKeyword
);

/// <summary>
/// C# language transpiler for the TypeGen system.
/// 
/// This class converts TGS (TypeGen Schema) definitions into C# class and enum definitions.
/// It handles:
/// - Type mapping from TGS types to C# types (e.g., Uid -> Guid, string -> string)
/// - Generating proper C# classes with properties and records
/// - Creating namespace hierarchy based on directory structure and project structure
/// - Automatic using directive generation for referenced types
/// - Generic type support (List&lt;T&gt;, Dictionary&lt;K,V&gt;, etc.)
/// - Nullable type support with nullable reference types
/// - Schema inheritance using C# class inheritance
/// - Project structure discovery (finding .sln files for namespace generation)
/// 
/// Generated files follow C# best practices:
/// - File-scoped namespaces (namespace MyProject.Entities;)
/// - Required properties for non-nullable fields
/// - Init-only properties for immutability
/// - Proper using directives for external types
/// - Automatic namespace resolution for cross-references
/// </summary>
public class Csharp : Lang<CsharpType>
{
    /// <summary>
    /// Maps TGS primitive types to their C# equivalents.
    /// This is the foundation of type translation between the two systems.
    /// Note that some types have specific C# mappings (e.g., Uid -> Guid, Date -> DateOnly).
    /// </summary>
    /// <returns>Dictionary mapping TGS type names to C# types with optional using directives</returns>
    protected override Dictionary<string, CsharpType> SetTypeTranslations()
    {
        return new Dictionary<string, CsharpType>
        {
            // Identifier types
            { TgType.Uid, new CsharpType("Guid", null) },
            
            // Basic types
            { TgType.String, new CsharpType("string", null) },
            { TgType.Bool, new CsharpType("bool", null) },
            { TgType.Char, new CsharpType("char", null) },
            
            // Numeric types
            { TgType.Int, new CsharpType("int", null) },
            { TgType.UInt, new CsharpType("uint", null) },
            { TgType.Long, new CsharpType("long", null) },
            { TgType.ULong, new CsharpType("ulong", null) },
            { TgType.Short, new CsharpType("short", null) },
            { TgType.UShort, new CsharpType("ushort", null) },
            { TgType.Byte, new CsharpType("byte", null) },
            { TgType.SByte, new CsharpType("sbyte", null) },
            { TgType.Float, new CsharpType("float", null) },
            { TgType.Double, new CsharpType("double", null) },
            { TgType.Decimal, new CsharpType("decimal", null) },
            
            // Complex types (these map to .NET collection types)
            { TgType.Object, new CsharpType("object", null) },
            { TgType.Array, new CsharpType("[]", null) },
            { TgType.List, new CsharpType("List", null) },
            { TgType.Map, new CsharpType("Dictionary", null) },
            { TgType.Set, new CsharpType("HashSet", null) },
            { TgType.Queue, new CsharpType("Queue", null) },
            
            // Date/time types (.NET 6+ DateOnly and DateTime)
            { TgType.Date, new CsharpType("DateOnly", null) },
            { TgType.DateTime, new CsharpType("DateTime", null) },
        };
    }

    // State maintained during transpilation
    private string _projectName = "";                     // Discovered project name from .sln file
    private const string FirstDirective = "public class"; // C# class declaration keyword
    private string _fullOutput = "";                      // Complete output path
    private string _namespacePrefix = "";                 // Base namespace for the project

    /// <summary>
    /// Main transpilation entry point. Processes all schema files and generates C# code.
    /// 
    /// The C# transpilation process:
    /// 1. Discover project structure and determine namespace prefix
    /// 2. For each schema file:
    ///    a. Setup type mappings for schemas and enums
    ///    b. Generate individual .cs files for each schema/enum
    ///    c. Handle namespace generation and using directives
    ///    d. Create proper directory structure matching namespaces
    /// 3. Return list of generated files for writing to disk
    /// 
    /// Unlike TypeScript, C# requires careful namespace management and project structure awareness.
    /// </summary>
    /// <param name="schemaFiles">Parsed schema files to transpile</param>
    /// <param name="outputPath">Base output directory path</param>
    /// <returns>List of generated C# files</returns>
    public override List<TranspiledFile> TranspileFiles(List<SchemaFile> schemaFiles, string outputPath)
    {
        var transpiledFiles = new List<TranspiledFile>();

        // Discover project structure for namespace generation
        _projectName = FindProjectSolutionFile(outputPath);
        _fullOutput = outputPath;
        _namespacePrefix = GetNamespacePrefix();

        // Process each schema file independently
        schemaFiles.ForEach(schemaFile =>
        {
            // Collect imported types for cross-reference
            var importsSchemas = schemaFile.Imports.SelectMany(i => i.Schemas).ToList();
            var importsEnums = schemaFile.Imports.SelectMany(i => i.Enums).ToList();

            // Add schemas and enums to type translation system temporarily
            AddSchemasToTypeTranslations(schemaFile.Schemas, importsSchemas);
            AddEnumsToTypeTranslations(schemaFile.Enums, importsEnums);

            // Generate C# classes for each schema
            schemaFile.Schemas.ForEach(schema =>
            {
                var schemaOutputPath = GetSchemaOutputPath(schema);
                var path = $"{outputPath}/{schemaOutputPath}";
                var content = new StringBuilder();

                // Generate the class definition
                AppendStartOfTheType(content, schema);
                AddProps(content, schema);
                content.AppendLine("}");

                var pathWithFile = $"{path}{schema.Name}.cs";
                // Add namespace declaration at the top
                AppendNamespace(content, GetSchemaNamespace(schemaOutputPath));
                transpiledFiles.Add(new TranspiledFile(pathWithFile, content.ToString()));
            });

            // Clean up temporary type mappings
            RemoveSchemasFromTypeTranslations(schemaFile.Schemas, importsSchemas);
            RemoveEnumsFromTypeTranslations(schemaFile.Enums, importsEnums);

            // Generate C# enums for each enum definition
            schemaFile.Enums.ForEach(e =>
            {
                var enumOutputPath = GetSchemaOutputPath(e);
                var path = $"{outputPath}/{enumOutputPath}";
                var content = new StringBuilder();

                AppendStartOfTheType(content, e);
                AppendValues(content, e);
                content.AppendLine("}");

                var pathWithFile = $"{path}{e.Name}.cs";
                // Add namespace declaration for enums
                AppendNamespace(content, GetSchemaNamespace(enumOutputPath), "public enum");
                transpiledFiles.Add(new TranspiledFile(pathWithFile, content.ToString()));
            });
        });

        return transpiledFiles;
    }

    #region Type Translation Management

    /// <summary>
    /// Temporarily adds enums to the type translation system.
    /// This allows enums to be referenced by schemas during generation.
    /// </summary>
    private void AddEnumsToTypeTranslations(List<TgEnum> schemaFileEnums, List<TgEnum> importsEnums)
    {
        schemaFileEnums.ForEach(AddEnumToTypeTranslations);
        importsEnums.ForEach(AddEnumToTypeTranslations);
    }

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
    /// Removes schemas from type translation system after processing.
    /// This prevents namespace pollution between different schema files.
    /// </summary>
    private void RemoveSchemasFromTypeTranslations(List<Schema> schemaFileSchemas, List<Schema> imports)
    {
        schemaFileSchemas.ForEach(s => TypeTranslations.Remove(s.Name));
        imports.ForEach(s => TypeTranslations.Remove(s.Name));
    }

    /// <summary>
    /// Removes enums from type translation system after processing.
    /// </summary>
    private void RemoveEnumsFromTypeTranslations(List<TgEnum> schemaFileEnums, List<TgEnum> importsEnums)
    {
        schemaFileEnums.ForEach(e => TypeTranslations.Remove(e.Name));
        importsEnums.ForEach(e => TypeTranslations.Remove(e.Name));
    }

    #endregion

    #region Project Structure Discovery

    /// <summary>
    /// Discovers the project name by searching for .sln files in parent directories.
    /// This is crucial for generating proper C# namespaces that match the project structure.
    /// 
    /// The search strategy:
    /// 1. Look for .sln files starting from the output path
    /// 2. Traverse up to 5 parent directories
    /// 3. Use the .sln filename as the project name
    /// 4. Fall back to directory name if no .sln is found
    /// 
    /// This ensures generated namespaces follow C# conventions like:
    /// MyProject.Entities.Customers rather than generic names.
    /// </summary>
    /// <param name="startPath">Starting directory path</param>
    /// <returns>Discovered project name</returns>
    private static string FindProjectSolutionFile(string startPath)
    {
        const int maxSearchDepth = 5;

        startPath = Path.GetFullPath(startPath);
        var originalRoot = Path.GetPathRoot(startPath);
        int currentDepth = 0;

        while (currentDepth < maxSearchDepth)
        {
            try
            {
                var slnFiles = Directory.GetFiles(startPath, "*.sln");
                if (slnFiles.Length != 0)
                {
                    return Path.GetFileNameWithoutExtension(slnFiles.First());
                }

                var parentDirectory = Directory.GetParent(startPath)?.FullName;

                if (string.IsNullOrEmpty(parentDirectory) || parentDirectory == originalRoot)
                {
                    break;
                }

                startPath = parentDirectory;
                currentDepth++;
            }
            catch (Exception)
            {
                // If we can't access a directory, create it and continue
                Directory.CreateDirectory(startPath);
            }
        }

        // Fallback: use the last directory segment as project name
        string[] segments = startPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        return segments.Length > 0 ? segments.Last() : "UnknownProject";
    }

    /// <summary>
    /// Adds a schema to the type translation system with proper namespace information.
    /// </summary>
    private void AddSchemaToTypeTranslations(Schema schema)
    {
        TypeTranslations.Add(schema.Name, new CsharpType(schema.Name, GetSchemaNamespace(schema.PathToOutput)));
    }

    /// <summary>
    /// Adds an enum to the type translation system with proper namespace information.
    /// </summary>
    private void AddEnumToTypeTranslations(TgEnum tgEnum)
    {
        TypeTranslations.Add(tgEnum.Name, new CsharpType(tgEnum.Name, GetSchemaNamespace(tgEnum.PathToOutput)));
    }

    #endregion

    #region Property Generation

    /// <summary>
    /// Generates C# properties for a schema.
    /// 
    /// For each property:
    /// 1. Determines the C# type
    /// 2. Handles complex/generic types
    /// 3. Generates using statements for referenced types
    /// 4. Writes the property definition with proper accessibility and nullability
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
                    // Simple type mapping (string, int, custom schema, etc.)
                    WriteProp(content, prop);
                    break;
                case var _ when propType.Contains('<'):
                    // Generic type like List<string> or Dictionary<string, Customer>
                    WriteComplexProp(content, prop);
                    break;
                default:
                    throw new TypeNotSupportedException(propType);
            }
        });
    }

    #endregion

    #region Namespace Management

    /// <summary>
    /// Generates the full namespace for a schema based on its output path.
    /// 
    /// Examples:
    /// - PathToOutput: "/Entities/Customers" -> "MyProject.Entities.Customers"
    /// - PathToOutput: "" -> "MyProject"
    /// - With namespace prefix: "MyCompany.MyProject.Entities.Customers"
    /// 
    /// The namespace follows C# conventions and matches the directory structure.
    /// </summary>
    /// <param name="schemaOutputPath">The output path from the schema definition</param>
    /// <returns>Complete namespace string</returns>
    private string GetSchemaNamespace(string schemaOutputPath)
    {
        // Split path and filter out empty segments
        var sopSplitted = schemaOutputPath
            .Split("/")
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        var namespaceToAppend = string.Join(".", sopSplitted);

        // Build full namespace with project name
        if (string.IsNullOrEmpty(_namespacePrefix))
        {
            return string.IsNullOrEmpty(namespaceToAppend)
                ? _projectName
                : $"{_projectName}.{namespaceToAppend}";
        }

        return string.IsNullOrEmpty(namespaceToAppend)
            ? $"{_namespacePrefix}"
            : $"{_namespacePrefix}.{namespaceToAppend}";
    }

    /// <summary>
    /// Determines the namespace prefix based on the project structure.
    /// This helps create proper namespace hierarchy when the output is nested within the project.
    /// </summary>
    /// <returns>Namespace prefix based on directory structure</returns>
    private string GetNamespacePrefix()
    {
        var fullOutputSegments = _fullOutput.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToArray();

        int projectNameIndex = Array.IndexOf(fullOutputSegments, _projectName);

        var namespaceSegments = fullOutputSegments
            .Skip(projectNameIndex)
            .ToArray();

        var namespacePrefix = string.Join(".", namespaceSegments);
        return namespacePrefix;
    }

    #endregion

    #region Class and Enum Generation

    /// <summary>
    /// Generates the opening of a C# class with optional inheritance.
    /// 
    /// Examples:
    /// - public class Customer {
    /// - public class Employee : Person {
    /// 
    /// Also handles cross-namespace inheritance by adding appropriate using directives.
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="schema">Schema definition</param>
    private void AppendStartOfTheType(StringBuilder content, Schema schema)
    {
        string inheritance = "";
        if (schema.Inheritance is not null)
        {
            var parentNamespace = GetSchemaNamespace(schema.Inheritance.PathToOutput);
            var childNamespace = GetSchemaNamespace(schema.PathToOutput);
            inheritance = $" : {schema.InheritanceName}";

            // Add using directive if parent is in different namespace
            if (!parentNamespace.Equals(childNamespace)
                && !parentNamespace.Equals(_namespacePrefix))
                content.Insert(0, $"using {parentNamespace};\n\n");
        }

        content.AppendLine($"{FirstDirective} {schema.Name}{inheritance} {{");
    }

    /// <summary>
    /// Generates the opening of a C# enum.
    /// Example: public enum CustomerStatus {
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="tgEnum">Enum definition</param>
    private void AppendStartOfTheType(StringBuilder content, TgEnum tgEnum)
    {
        content.AppendLine($"public enum {tgEnum.Name} {{");
    }

    /// <summary>
    /// Gets the output path for a schema, ensuring it follows absolute path rules.
    /// </summary>
    private static string GetSchemaOutputPath(Schema schema)
    {
        return string.IsNullOrEmpty(schema.PathToOutput) ? "" : $"{schema.PathToOutput}/";
    }

    /// <summary>
    /// Gets the output path for an enum, ensuring it follows absolute path rules.
    /// </summary>
    private static string GetSchemaOutputPath(TgEnum tgEnum)
    {
        return string.IsNullOrEmpty(tgEnum.PathToOutput) ? "" : $"{tgEnum.PathToOutput}/";
    }

    /// <summary>
    /// Adds the namespace declaration to the top of a C# file.
    /// Uses file-scoped namespace syntax (C# 10+): namespace MyProject.Entities;
    /// </summary>
    /// <param name="content">StringBuilder to modify</param>
    /// <param name="namespaceToAppend">Namespace to add</param>
    /// <param name="directive">The type directive to look for (class or enum)</param>
    private void AppendNamespace(StringBuilder content, string namespaceToAppend, string directive = FirstDirective)
    {
        var indexToAppendLine = content.ToString().IndexOf(directive, StringComparison.Ordinal);
        content.Insert(indexToAppendLine, $"namespace {namespaceToAppend};\n\n");
    }

    #endregion

    #region Complex Type Handling

    /// <summary>
    /// Writes a property with a complex/generic type like List&lt;string&gt; or Dictionary&lt;K,V&gt;.
    /// </summary>
    private void WriteComplexProp(StringBuilder content, TgProperty prop)
    {
        var (csharpTypeTranslation, usingKeyword) = TranslateComplexTypeRec(prop.Type.Trim());
        AppendProp(content, prop, usingKeyword, csharpTypeTranslation);
    }

    /// <summary>
    /// Recursively translates complex generic types to C#.
    /// 
    /// Examples:
    /// - Array&lt;string&gt; -> string[]
    /// - List&lt;Customer&gt; -> List&lt;Customer&gt;
    /// - Dictionary&lt;string, List&lt;Customer&gt;&gt; -> Dictionary&lt;string, List&lt;Customer&gt;&gt;
    /// 
    /// Handles nested generics correctly and manages using directives for complex types.
    /// </summary>
    /// <param name="propType">TGS type to translate</param>
    /// <returns>C# type representation with using directive</returns>
    private CsharpType TranslateComplexTypeRec(string propType)
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

        // Special handling for Array type - convert to C# array syntax
        if (outerTypeName == "Array")
        {
            var innerType = TranslateComplexTypeRec(typeParams[0].Trim());
            return innerType with { CsharpTypeTranslation = $"{innerType.CsharpTypeTranslation}[]" };
        }

        // Generic type translation
        var csharpType = TypeTranslations[outerTypeName];

        var translatedParams = typeParams.Select(param => TranslateComplexTypeRec(param.Trim())).ToList();

        var typeTranslated =
            $"{csharpType.CsharpTypeTranslation}<{string.Join(", ", translatedParams.Select(t => t.CsharpTypeTranslation))}>";

        // Combine using directives from all type parameters
        var usingKeyword =
            CombineUsingKeywords(csharpType.UsingKeyword!, translatedParams.Select(t => t.UsingKeyword).ToList()!);

        return new CsharpType(typeTranslated, usingKeyword);
    }

    /// <summary>
    /// Splits generic type parameters, respecting nested generics.
    /// 
    /// Example: "string, Dictionary&lt;string, Customer&gt;, bool"
    /// Returns: ["string", "Dictionary&lt;string, Customer&gt;", "bool"]
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
    /// Combines multiple using directives into a single string.
    /// Prevents duplicate using statements in the generated code.
    /// </summary>
    /// <param name="baseUsing">Base using directive</param>
    /// <param name="additionalUsings">Additional using directives to combine</param>
    /// <returns>Combined using directives</returns>
    private static string CombineUsingKeywords(string baseUsing, List<string> additionalUsings)
    {
        var combinedUsing = baseUsing;

        foreach (var additionalUsing in additionalUsings.Where(u => !string.IsNullOrEmpty(u)))
        {
            if (string.IsNullOrEmpty(combinedUsing))
            {
                combinedUsing = additionalUsing;
            }
            else if (!combinedUsing.Contains(additionalUsing))
            {
                combinedUsing = $"{combinedUsing}\n{additionalUsing}";
            }
        }

        return combinedUsing;
    }

    #endregion

    #region Property and Value Generation

    /// <summary>
    /// Writes a property with a simple type mapping.
    /// </summary>
    private void WriteProp(StringBuilder content, TgProperty prop)
    {
        var (csharpTypeTranslation, usingKeyword) = TypeTranslations[prop.Type];
        AppendProp(content, prop, usingKeyword, csharpTypeTranslation);
    }

    /// <summary>
    /// Appends a property definition to the C# class.
    /// 
    /// Examples:
    /// - public required string Name { get; init; }
    /// - public string? Email { get; init; }
    /// 
    /// Uses modern C# features:
    /// - required modifier for non-nullable properties
    /// - init-only setters for immutability
    /// - nullable reference types
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="prop">Property definition</param>
    /// <param name="usingKeyword">Using directive for the property type</param>
    /// <param name="csharpTypeTranslation">C# type string</param>
    private void AppendProp(StringBuilder content, TgProperty prop, string? usingKeyword,
        string csharpTypeTranslation)
    {
        // Add using directive if needed and not already present
        if (!string.IsNullOrEmpty(usingKeyword)
            && !content.ToString().Contains(usingKeyword)
            && !usingKeyword.Equals(_namespacePrefix))
        {
            content.Insert(0, $"using {usingKeyword};\n");
        }

        // Generate property with appropriate nullability
        if (prop.IsNullable)
        {
            content.AppendLine($"    public {csharpTypeTranslation}? {prop.Name} {{ get; init; }}");
        }
        else
        {
            content.AppendLine($"    public required {csharpTypeTranslation} {prop.Name} {{ get; init; }}");
        }
    }

    /// <summary>
    /// Appends enum values to the C# enum definition.
    /// 
    /// Example output:
    ///     Active,
    ///     Inactive,
    ///     Pending
    /// </summary>
    /// <param name="content">StringBuilder to append to</param>
    /// <param name="tgEnum">Enum definition with values</param>
    private void AppendValues(StringBuilder content, TgEnum tgEnum)
    {
        tgEnum.Values.ForEach(value =>
        {
            var isLast = value == tgEnum.Values.Last();
            content.AppendLine($"    {value}{(isLast ? "" : ",")}");
        });
    }

    #endregion
}