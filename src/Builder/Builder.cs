using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using typegen.Builder.Langs;
using typegen.Builder.Lexer;
using typegen.Builder.Parser;
using typegen.Builder.Types;
using typegen.Constants;
using Cts = typegen.Constants.Constants;

namespace typegen.Builder;

/// <summary>
/// Core builder class that orchestrates the entire TypeGen compilation process.
/// 
/// The build process follows these main steps:
/// 1. Load and validate configuration file (typegen.config.json)
/// 2. Discover all .tgs files in the project directory
/// 3. Parse each .tgs file using the lexer/parser pipeline
/// 4. Resolve imports between schema files
/// 5. Transpile to target languages based on configuration
/// 
/// This class handles:
/// - Configuration management and validation
/// - File discovery and reading
/// - Import resolution between schema files
/// - Orchestrating language-specific transpilation
/// - Error reporting and validation
/// </summary>
public static class Builder
{

    /// <summary>
    /// Loads and validates the TypeGen configuration file.
    /// The config file specifies target languages, output paths, and build settings.
    /// </summary>
    /// <returns>Validated configuration object</returns>
    /// <exception cref="Exception">Thrown if config file is missing or invalid</exception>
    public static ConfigFile GetConfig()
    {
        var configFilePath = Cts.providedPath + Cts.ConfigFileName;
        if (!File.Exists(configFilePath))
            throw new Exception($"Config file '{Cts.ConfigFileName}' not found in {(string.IsNullOrEmpty(Cts.providedPath) ? "the current directory" : Path.GetFullPath(Cts.providedPath))}.");

        var jsonString = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<ConfigFile>(jsonString);

        if (config == null) throw new Exception("Error parsing config file.");

        return CheckConfigFile(config);
    }

    /// <summary>
    /// Validates and normalizes the configuration file.
    /// Ensures all required fields are present and sets defaults where needed.
    /// </summary>
    /// <param name="config">Raw configuration object</param>
    /// <returns>Validated and normalized configuration</returns>
    private static ConfigFile CheckConfigFile(ConfigFile config)
    {
        var didConfigChanged = false;
        foreach (var buildOutput in config.BuildOut)
        {
            if (!Cts.SupportedLangs.ContainsValue(buildOutput.Lang))
                throw new Exception($"Language '{buildOutput.Lang}' not supported.");
            if (!buildOutput.OutputPath.Contains('\\')) continue;
            didConfigChanged = true;
            // Updates the output path replacing "\" with "/"
            buildOutput.OutputPath = buildOutput.OutputPath.Replace('\\', '/');
        }

        if (!didConfigChanged) return config;

        // Updates the config file with the correct path
        var configFilePath = Cts.providedPath + Cts.ConfigFileName;
        var newJsonString = JsonSerializer.Serialize(config, Cts.DefaultJsonOptions);
        File.WriteAllText(configFilePath, newJsonString, Encoding.UTF8);
        return config;
    }

    /// <summary>
    /// Discovers all .tgs files in the current directory and subdirectories,
    /// then parses them into SchemaFile objects.
    /// 
    /// This method:
    /// 1. Recursively searches for *.tgs files
    /// 2. Parses each file using the lexer/parser pipeline
    /// 3. Resolves imports between files
    /// </summary>
    /// <returns>List of parsed schema files with resolved imports</returns>
    public static List<SchemaFile> ReadSchemaFiles()
    {
        string currentDirectory = Cts.providedPath != "" ? Cts.providedPath : Directory.GetCurrentDirectory();

        var schemaFilePaths = Directory.GetFiles(currentDirectory, "*.tgs", SearchOption.AllDirectories);

        var schemaFiles = schemaFilePaths.Select(ParseSchemaFile).ToList();

        ResolveSchemaInheritances(schemaFiles);

        ResolveImports(schemaFiles, currentDirectory);
        return schemaFiles;
    }

    /// <summary>
    /// Parses a single .tgs file using the lexer and parser.
    /// This is where the magic happens - raw text becomes structured data.
    /// </summary>
    /// <param name="filePath">Path to the .tgs file to parse</param>
    /// <returns>Parsed schema file object</returns>
    /// <exception cref="Exception">Thrown if parsing fails</exception>
    private static SchemaFile ParseSchemaFile(string filePath)
    {
        var fileContent = File.ReadAllText(filePath);

        var lexer = new TgsLexer(fileContent);
        var tokens = lexer.Tokenize();

        var parser = new TgsParser(tokens, filePath);
        var schemaFile = parser.Parse();

        return schemaFile;
    }

    /// <summary>
    /// Resolves import statements between schema files.
    /// 
    /// When a schema file imports from another file (e.g., import { Customer } from "./customers.tgs"),
    /// this method:
    /// 1. Resolves the relative import path to an absolute file path
    /// 2. Finds the corresponding parsed schema file
    /// 3. Locates the specific schemas/enums being imported
    /// 4. Adds them to the importing file's import definitions
    /// 
    /// This enables cross-file references and modular schema organization.
    /// </summary>
    /// <param name="schemaFiles">All parsed schema files in the project</param>
    private static void ResolveImports(List<SchemaFile> schemaFiles, string currentDirectory)
    {
        foreach (var schemaFile in schemaFiles)
        {
            foreach (var import in schemaFile.Imports)
            {
                var absoluteImportPath = Path.GetFullPath(Path.Combine(currentDirectory, import.Path));

                var importedSchemaFile = schemaFiles.FirstOrDefault(sf => sf.Path == absoluteImportPath) ?? throw new Exception($"Imported schema file '{import.Path}' not found. Resolved path: {absoluteImportPath}");

                foreach (var importName in import.ImportNames)
                {
                    var schema = importedSchemaFile.Schemas.FirstOrDefault(s => s.Name == importName);
                    var tgEnum = importedSchemaFile.Enums.FirstOrDefault(e => e.Name == importName);
                    if (schema != null)
                    {
                        import.Schemas.Add(schema);
                    }
                    else if (tgEnum != null)
                    {
                        import.Enums.Add(tgEnum);
                    }
                    else
                    {
                        if (!AnySimilar(importName, importedSchemaFile.Schemas.Select(s => s.Name)))
                            throw new Exception($"Imported {importName} not found in file '{import.Path}'.");
                    }
                }
            }
        }
    }

    private static bool AnySimilar(string importName, IEnumerable<string> availableNames)
    {
        var similarNames = availableNames
            .Where(name => CalculateNameSimilarity(importName, name).IsMatch)
            .ToList();

        if (similarNames.Count == 0) return false;

        var suggestions = string.Join(", ", similarNames);
        throw new Exception($"Import '{importName}' not found. Did you mean: {suggestions}?");
    }

    /// <summary>
    /// Calculates the similarity between two names using edit distance and other metrics.
    /// </summary>
    private static (bool IsMatch, bool IsExactMatch, double Score) CalculateNameSimilarity(string firstName, string secondName)
    {
        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(secondName))
            return (false, false, 0);

        if (firstName == secondName)
            return (true, true, 1.0);

        var editDistance = CalculateEditDistance(firstName, secondName);
        var longestNameLength = Math.Max(firstName.Length, secondName.Length);
        var editSimilarity = 1 - ((double)editDistance / longestNameLength);

        var nameContainsOther = firstName.Contains(secondName) ||
                               secondName.Contains(firstName);

        var similarityScore = editSimilarity;
        if (nameContainsOther) similarityScore = Math.Max(similarityScore, 0.9);

        const double SIMILARITY_THRESHOLD = 0.70;
        return (
            IsMatch: similarityScore >= SIMILARITY_THRESHOLD,
            IsExactMatch: similarityScore >= SIMILARITY_THRESHOLD,
            Score: similarityScore
        );
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int CalculateEditDistance(string source, string target)
    {
        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distanceMatrix = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; i++)
            distanceMatrix[i, 0] = i;
        for (var j = 0; j <= targetLength; j++)
            distanceMatrix[0, j] = j;

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var substitutionCost = (source[i - 1] == target[j - 1]) ? 0 : 1;

                distanceMatrix[i, j] = Math.Min(
                    Math.Min(
                        distanceMatrix[i - 1, j] + 1,
                        distanceMatrix[i, j - 1] + 1),
                    distanceMatrix[i - 1, j - 1] + substitutionCost
                );
            }
        }

        return distanceMatrix[sourceLength, targetLength];
    }

    /// <summary>
    /// Gets the history file path based on the provided path.
    /// </summary>
    private static string GetHistoryFileName()
    {
        return Path.Combine(Cts.providedPath + Cts.TypeGenFolderName, "typegen.history.json");
    }

    /// <summary>
    /// Main transpilation method that generates code in target languages.
    /// 
    /// For each build configuration:
    /// 1. Instantiates the appropriate language transpiler
    /// 2. Converts schema files to target language code
    /// 3. Creates output directory structure
    /// 4. Writes generated files to disk
    /// 
    /// The transpiler handles language-specific concerns like:
    /// - Type mappings
    /// - Import statement generation
    /// - File naming conventions
    /// - Code formatting and style
    /// </summary>
    /// <param name="schemaFiles">Parsed and resolved schema files</param>
    /// <param name="configFile">Build configuration</param>
    public static void Transpile(List<SchemaFile> schemaFiles, ConfigFile configFile, bool encoderShouldEmitUTF8Identifier)
    {
        var history = LoadHistory();
        var updatedHistory = new Dictionary<string, List<string>>();

        foreach (var buildOutput in configFile.BuildOut)
        {
            var lang = GetLang(Cts.SupportedLangs.FirstOrDefault(x => x.Value == buildOutput.Lang).Key);
            var transpiledFiles = lang.TranspileFiles(schemaFiles, buildOutput.OutputPath);

            foreach (var transpiledFile in transpiledFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(transpiledFile.Path) ??
                                          throw new InvalidOperationException());

                File.WriteAllText(transpiledFile.Path, transpiledFile.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier));

                if (!updatedHistory.ContainsKey(transpiledFile.Path))
                    updatedHistory[transpiledFile.Path] = [];

                updatedHistory[transpiledFile.Path].Add(transpiledFile.Path);
            }
        }

        DeleteObsoleteFiles(history, updatedHistory);
        SaveHistory(updatedHistory);
    }

    private static Dictionary<string, List<string>> LoadHistory()
    {
        var historyFileName = GetHistoryFileName();
        if (!File.Exists(historyFileName)) return [];

        var jsonString = File.ReadAllText(historyFileName);
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(jsonString) ?? [];
    }

    private static void SaveHistory(Dictionary<string, List<string>> updatedHistory)
    {
        var historyFileName = GetHistoryFileName();
        Directory.CreateDirectory(Path.GetDirectoryName(historyFileName) ?? throw new InvalidOperationException());
        var jsonString = JsonSerializer.Serialize(updatedHistory, Cts.DefaultJsonOptions);
        File.WriteAllText(historyFileName, jsonString, Encoding.UTF8);
    }

    private static void DeleteObsoleteFiles(Dictionary<string, List<string>> oldHistory,
        Dictionary<string, List<string>> updatedHistory)
    {
        var directoriesToCheck = new HashSet<string>();

        foreach (var (oldSchemaPath, oldFiles) in oldHistory)
        {
            if (!updatedHistory.TryGetValue(oldSchemaPath, out var currentFiles))
            {
                // The schema was removed, delete all its files
                foreach (var oldFile in oldFiles.Where(File.Exists))
                {
                    var directory = Path.GetDirectoryName(oldFile);
                    if (directory != null)
                    {
                        directoriesToCheck.Add(directory);
                    }
                    File.Delete(oldFile);
                }
            }
            else
            {
                // Schema exists, check for removed files
                foreach (var oldFile in oldFiles.Except(currentFiles))
                {
                    if (!File.Exists(oldFile)) continue;
                    var directory = Path.GetDirectoryName(oldFile);
                    if (directory != null)
                    {
                        directoriesToCheck.Add(directory);
                    }
                    File.Delete(oldFile);
                }
            }
        }

        // After all files are deleted, check and clean up empty directories
        foreach (var directory in directoriesToCheck.OrderByDescending(d => d.Length)) // Process deepest directories first
        {
            DeleteEmptyDirectory(directory);
        }
    }

    private static void DeleteEmptyDirectory(string directory)
    {
        while (true)
        {
            if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any()) return;

            Directory.Delete(directory);

            // After deleting this directory, check if parent is now empty
            var parent = Path.GetDirectoryName(directory);
            if (!string.IsNullOrEmpty(parent))
            {
                directory = parent;
                continue;
            }

            break;
        }
    }

    private static ILang GetLang(SupportedLanguages buildOutputLang)
    {
        return buildOutputLang switch
        {
            SupportedLanguages.CSharp => new Csharp(),
            SupportedLanguages.Typescript => new Typescript(),
            _ => throw new Exception($"Language '{buildOutputLang}' not supported.")
        };
    }

    private static void ResolveSchemaInheritances(List<SchemaFile> schemaFiles)
    {
        var allSchemas = schemaFiles.SelectMany(sf => sf.Schemas).ToList();

        var schemasWithInheritance = allSchemas
            .Where(s => !string.IsNullOrEmpty(s.InheritanceName))
            .ToList();

        foreach (var inheritingSchema in schemasWithInheritance)
        {
            var parentSchema = allSchemas
                .FirstOrDefault(s => s.Name == inheritingSchema.InheritanceName);

            if (parentSchema == null) continue;

            inheritingSchema.Inheritance = parentSchema;
        }
    }
}