using System.Text.Json;
using typegen.Builder.Lexer;
using typegen.Builder.Parser;
using typegen.Builder.Types;
using Bld = typegen.Builder.Builder;
using Cts = typegen.Constants.Constants;

namespace typegen;

/// <summary>
/// Main entry point for the TypeGen CLI application.
/// </summary>
public static class Program
{
    private static bool _shouldExit;

    /// <summary>
    /// Main entry point. Handles command-line arguments and routes to appropriate commands.
    /// Supports: build, init, create-project, --help
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static void Main(string[] args)
    {
        // Store args globally and preserve console color for restoration
        Cts.Args = args;
        Cts.DefaultConsoleColor = Console.ForegroundColor;

        // Set up graceful shutdown on Ctrl+C
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _shouldExit = true;
            Console.WriteLine("\nInterrupted");
            Environment.Exit(130); // Standard exit code for SIGINT
        };

        // Validate minimum arguments
        if (args.Length < 1)
        {
            PrintBadArgs();
            return;
        }

        var command = args[0];

        // Handle command-specific help (e.g., "typegen build --help")
        if (command != "--help" && command != "-h" && (args.Contains("--help") || args.Contains("-h")))
        {
            PrintHelp(command);
            return;
        }

        // Route to appropriate command handler
        switch (command)
        {
            case "build":
                Build();
                break;
            case "--help" or "-h":
                PrintHelp();
                break;
            case "init":
                Init();
                break;
            case "parse":
                Parse();
                break;
            case "create-project":
                if (args.Length > 1)
                {
                    CreateProject(args[1]);
                    break;
                }
                CreateProject(null);
                break;
            case "--version":
                Console.WriteLine("Version 1.0.0");
                break;
            default:
                PrintBadArgs(command);
                break;
        }
    }


    /// <summary>
    /// Parses .tgs files and outputs validation results.
    /// Usage: typegen parse <file-path> [--json] [--pretty] [--daemon]
    /// </summary>
    private static void Parse()
    {
        var isDaemon = Cts.Args.Contains("--daemon");
        var outputJson = Cts.Args.Contains("--json");
        var pretty = Cts.Args.Contains("--pretty");
        try
        {

            if (isDaemon)
            {
                ParseDaemon(outputJson, pretty);
            }
            else
            {
                // Original single-file parsing logic
                ParseSingleFile(outputJson, pretty);
            }
        }
        catch (Exception ex)
        {
            HandleParseError(ex, outputJson, pretty);
        }
    }



    private static void ParseDaemon(bool outputJson, bool pretty)
    {
        // Signal that daemon is ready
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "ready" }, pretty ? Cts.DefaultJsonOptions : JsonSerializerOptions.Default));
        }
        else
        {
            Console.WriteLine("Typegen daemon ready. Enter JSON with content field to parse (or 'exit' to quit):");
        }
        Console.Out.Flush();

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            line = line.Trim();

            // Exit command
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            // Skip empty lines
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            try
            {
                // Parse JSON input to extract content
                var jsonDoc = JsonDocument.Parse(line);
                if (jsonDoc.RootElement.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString();
                    if (content != null)
                    {
                        ParseContentAndOutput(content, "daemon-input", outputJson, pretty);
                    }
                    else
                    {
                        OutputError(new Exception("Content field is null"), outputJson, pretty);
                    }
                }
                else
                {
                    OutputError(new Exception("JSON must contain a 'content' field"), outputJson, pretty);
                }
            }
            catch (JsonException ex)
            {
                OutputError(new Exception($"Invalid JSON: {ex.Message}"), outputJson, pretty);
            }
            catch (Exception ex)
            {
                OutputError(ex, outputJson, pretty);
            }

            // Flush output to ensure immediate response
            Console.Out.Flush();
        }
    }

    private static void ParseSingleFile(bool outputJson, bool pretty)
    {
        // Expect file path as second argument
        if (Cts.Args.Length < 2)
        {
            Console.Error.WriteLine("Usage: typegen parse <file-path> [--json] [--daemon]");
            Environment.Exit(1);
            return;
        }

        var filePath = Cts.Args[1];
        ParseFileAndOutput(filePath, outputJson, pretty);
    }

    private static void ParseFileAndOutput(string filePath, bool outputJson, bool pretty)
    {
        // Check if file exists
        if (!File.Exists(filePath))
        {
            var error = $"File not found: {filePath}";
            if (outputJson)
            {
                var errorResult = new { success = false, errors = new[] { error }, file = filePath };
                Console.WriteLine(JsonSerializer.Serialize(errorResult, pretty ? Cts.DefaultJsonOptions : JsonSerializerOptions.Default));
            }
            else
            {
                Console.Error.WriteLine(error);
            }
            return; // Don't exit in daemon mode
        }

        // Read and parse the file
        var content = File.ReadAllText(filePath);
        var lexer = new TgsLexer(content);
        var tokens = lexer.Tokenize();
        var parser = new TgsParser(tokens, filePath);

        // Parse the file
        var schemaFile = parser.Parse(true);

        // Success output
        if (outputJson)
        {
            var result = new
            {
                success = true,
                errors = new string[0],
                schemas = schemaFile.Schemas.Count,
                enums = schemaFile.Enums.Count,
                imports = schemaFile.Imports.Count,
                file = filePath
            };
            Console.WriteLine(JsonSerializer.Serialize(result, pretty ? Cts.DefaultJsonOptions : JsonSerializerOptions.Default));
        }
        else
        {
            Console.WriteLine($"✓ {filePath}: {schemaFile.Schemas.Count} schemas, {schemaFile.Enums.Count} enums");
        }
    }

    private static void ParseContentAndOutput(string content, string sourceName, bool outputJson, bool pretty)
    {
        // Parse the content directly using the new constructor
        var parser = new TgsParser(content, sourceName);

        // Parse the content
        var schemaFile = parser.Parse(true);

        // Success output
        if (outputJson)
        {
            var result = new
            {
                success = true,
                errors = Array.Empty<string>(),
                schemas = schemaFile.Schemas.Count,
                enums = schemaFile.Enums.Count,
                imports = schemaFile.Imports.Count,
                source = sourceName
            };
            Console.WriteLine(JsonSerializer.Serialize(result, pretty ? Cts.DefaultJsonOptions : JsonSerializerOptions.Default));
        }
        else
        {
            Console.WriteLine($"✓ {sourceName}: {schemaFile.Schemas.Count} schemas, {schemaFile.Enums.Count} enums");
        }
    }

    private static void OutputError(Exception ex, bool outputJson, bool pretty)
    {
        var errors = ExtractErrorsDaemon(ex);

        if (outputJson)
        {
            var result = new { success = false, errors };
            Console.WriteLine(JsonSerializer.Serialize(result, pretty ? Cts.DefaultJsonOptions : JsonSerializerOptions.Default));
        }
        else
        {
            Console.Error.WriteLine($"Parse error: {ex.Message}");
            if (Cts.Args.Contains("--debug"))
            {
                Console.Error.WriteLine($"{ex.StackTrace}");
            }
        }
    }

    private static void HandleParseError(Exception ex, bool pretty, bool json)
    {
        OutputError(ex, json, pretty);

        // Only exit if not in daemon mode
        if (!Cts.Args.Contains("--daemon"))
        {
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Extracts individual parse errors from exception message.
    /// </summary>
    /// <param name="ex">Exception containing parse errors</param>
    /// <returns>Array of individual error messages</returns>
    private static string[] ExtractErrorsDaemon(Exception ex)
    {
        var message = ex.Message;
        return message.Split("<ERROR>", StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim())
                         .Where(line => !string.IsNullOrEmpty(line))
                         .ToArray();
    }

    /// <summary>
    /// Creates a new TypeGen project with the specified name.
    /// Sets up directory structure, config file, and .gitignore.
    /// </summary>
    /// <param name="projectName">Name of the project to create (will prompt if null)</param>
    private static void CreateProject(string? projectName)
    {
        // Prompt for project name if not provided
        while (string.IsNullOrEmpty(projectName) || string.IsNullOrWhiteSpace(projectName))
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("Project name: ");
            Console.ForegroundColor = Cts.DefaultConsoleColor;
            projectName = Console.ReadLine();

            // Check for graceful shutdown during input
            if (_shouldExit)
            {
                Console.ForegroundColor = Cts.DefaultConsoleColor;
                return;
            }
        }

        projectName = projectName.Trim();
        Init(projectName);

        // Success feedback
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Created project '{projectName}'");
        Console.ForegroundColor = Cts.DefaultConsoleColor;
        Console.WriteLine($"  Configuration: {projectName}/{Cts.ConfigFileName}");
    }

    /// <summary>
    /// Main build command - reads .tgs files, parses them, and generates output files.
    /// This is the core functionality of TypeGen.
    /// </summary>
    private static void Build()
    {
        try
        {
            SaveProvidedPath(1);
            // 1. Load configuration file (typegen.config.json)
            var configFile = Bld.GetConfig();

            // 2. Discover and parse all .tgs files in the project
            var schemaFiles = Bld.ReadSchemaFiles();

            var encoderShouldEmitUTF8Identifier = Cts.Args.Contains("--utf8-identifier");

            // 3. Generate output files according to config
            Bld.Transpile(schemaFiles, configFile, encoderShouldEmitUTF8Identifier);

            // Success feedback
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Build completed successfully");
            Console.ForegroundColor = Cts.DefaultConsoleColor;

            // Optional debug output
            if (Cts.Args.Contains("--print-output"))
            {
                Console.WriteLine("Output:");
                PrintSchemaFiles(schemaFiles);
            }
        }
        catch (Exception e)
        {
            // Formatted error output with optional stack trace
            Console.ForegroundColor = ConsoleColor.Red;
            var (title, messages) = ExtractErrors(e);
            Console.WriteLine($"{title}");
            Console.ForegroundColor = Cts.DefaultConsoleColor;
            foreach (var message in messages)
            {
                Console.WriteLine($"{message}");
            }
            if (Cts.Args.Contains("--debug"))
            {
                Console.WriteLine($"  {e.StackTrace}");
            }
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Extracts individual parse errors from exception message.
    /// </summary>
    /// <param name="ex">Exception containing parse errors</param>
    /// <returns>Tuple of title and array of individual error messages</returns>
    private static (string title, string[] errors) ExtractErrors(Exception e)
    {
        var message = e.Message.Split("<ERRORTITLE>");
        if (message.Length < 1)
        {
            return (e.Message, new string[0]);
        }
        var errors = message[1].Split("<ERROR>");

        return (message[0], errors.Select(e => e.Trim()).Where(line => !string.IsNullOrEmpty(line)).ToArray());
    }

    /// <summary>
    /// Initializes TypeGen in the current directory.
    /// Creates config file, .typegen folder, and .gitignore entry.
    /// </summary>
    private static void Init()
    {
        SaveProvidedPath(1);
        var configFilePath = Cts.providedPath + Cts.ConfigFileName;
        var typegenFolderPath = Cts.providedPath + Cts.TypeGenFolderName;
        var gitignoreFilePath = Cts.providedPath + ".gitignore";

        // Create default config file if it doesn't exist
        if (!File.Exists(configFilePath))
        {
            var configFile = new ConfigFile(
                "https://raw.githubusercontent.com/cakeruu/typegen/main/json-schema/typegen-config-schema.json",
                [new BuildOut("", "")]);
            var json = JsonSerializer.Serialize(configFile, Cts.DefaultJsonOptions);

            File.WriteAllText(configFilePath, json);
        }

        // Create .typegen directory for internal files
        if (!Directory.Exists(typegenFolderPath))
        {
            Directory.CreateDirectory(typegenFolderPath);
        }

        // Add .typegen to .gitignore to avoid committing generated files
        File.WriteAllText(gitignoreFilePath, Cts.TypeGenFolderName);
    }

    /// <summary>
    /// Initializes TypeGen in a new project directory.
    /// </summary>
    /// <param name="projectName">Name of the project directory to create</param>
    private static void Init(string projectName)
    {
        SaveProvidedPath(2);
        var projectPath = Cts.providedPath + projectName;
        Directory.CreateDirectory(projectPath);

        // Create config file in new project directory
        var configFile = new ConfigFile(
            "https://raw.githubusercontent.com/cakeruu/typegen/main/json-schema/typegen-config-schema.json",
            [new BuildOut("", "")]);
        var json = JsonSerializer.Serialize(configFile, Cts.DefaultJsonOptions);

        File.WriteAllText($"{projectPath}/{Cts.ConfigFileName}", json);

        Directory.CreateDirectory($"{projectPath}/{Cts.TypeGenFolderName}");

        File.WriteAllText($"{projectPath}/.gitignore", Cts.TypeGenFolderName);
    }

    private static void SaveProvidedPath(int index)
    {
        while (index < Cts.Args.Length && Cts.Args[index].StartsWith("--"))
        {
            index++;
        }

        if (Cts.Args.Length <= index)
        {
            return;
        }

        Cts.providedPath = Cts.Args[index];
        if (!Cts.providedPath.EndsWith("/"))
        {
            Cts.providedPath += "/";
        }
    }

    /// <summary>
    /// Debug utility to print discovered schema files and their contents.
    /// Used with --print-output flag.
    /// </summary>
    /// <param name="schemaFiles">Parsed schema files to display</param>
    private static void PrintSchemaFiles(List<SchemaFile> schemaFiles)
    {
        schemaFiles.ForEach(schemaFile =>
        {
            Console.WriteLine($"Schema file found: {schemaFile.Path}\n");
            schemaFile.Schemas.ForEach(schema =>
            {
                Console.WriteLine($"Generating schema {schema.Name} to {schema.PathToOutput}");
                Console.WriteLine($"    Inheritance: {schema.Inheritance?.Name ?? "None"}");
                Console.WriteLine($"    Properties:");
                schema.Props.ToList().ForEach(prop => { Console.WriteLine($"        {prop.Name}: {prop.Type}"); });
                Console.WriteLine();
            });
        });

        Console.WriteLine($"Found schemas: {schemaFiles.SelectMany(s => s.Schemas).ToList().Count}");
    }

    #region CLI Help and Error Messages

    /// <summary>
    /// Displays error for unknown commands.
    /// </summary>
    /// <param name="s">The unknown command that was entered</param>
    private static void PrintBadArgs(string s)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"'{s}' is not a typegen command.");
        Console.WriteLine();
        Console.ForegroundColor = Cts.DefaultConsoleColor;
        Console.WriteLine("See 'typegen --help' for available commands.");
    }

    /// <summary>
    /// Displays error when no command is provided.
    /// </summary>
    private static void PrintBadArgs()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Missing command");
        Console.WriteLine();
        Console.ForegroundColor = Cts.DefaultConsoleColor;
        Console.WriteLine("Usage: typegen <command>");
        Console.WriteLine();
        Console.WriteLine("See 'typegen --help' for available commands.");
    }

    /// <summary>
    /// Displays general help information with all available commands.
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("Usage: typegen <command>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine($"{"  <build> [path]",-35} Generate types from schema files, if no path is provided, the current directory will be used");
        Console.WriteLine($"{"  <parse> [path]",-35} Parse a .tgs file and outputs errors/success status");
        Console.WriteLine($"{"  <init>",-35} Initialize typegen in current directory");
        Console.WriteLine($"{"  <create-project> [name]",-35} Create a new typegen project, if no name is provided, the cli will prompt for a name");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"{"  -h, --help",-35} Show this help message");
    }

    /// <summary>
    /// Displays command-specific help information.
    /// </summary>
    /// <param name="command">The command to show help for</param>
    private static void PrintHelp(string command)
    {
        Console.WriteLine($"Usage: typegen {command}");
        Console.WriteLine();
        switch (command)
        {
            case "build":
                PrintBuildHelp();
                break;
            case "init":
                PrintInitHelp();
                break;
            case "create-project":
                PrintCreateProjectHelp();
                break;
            case "parse":
                PrintParseHelp();
                break;
            default:
                PrintBadArgs(command);
                break;
        }
    }

    private static void PrintBuildHelp()
    {
        Console.WriteLine($"{"  <build> [path]",-35} Generate types from schema files, if no path is provided, the current directory will be used");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"{"  --debug",-35} Print debug information");
        Console.WriteLine($"{"  --print-output",-35} Print output to console");
        Console.WriteLine($"{"  --utf8-identifier",-35} Emit UTF-8 identifier in output files");
    }

    private static void PrintInitHelp()
    {
        Console.WriteLine($"{"  <init>",-35} Initialize typegen in current directory");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("No options available");
    }

    private static void PrintCreateProjectHelp()
    {
        Console.WriteLine($"{"  <create-project> [name]",-35} Create a new typegen project, if no name is provided, the cli will prompt for a name");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("No options available");
    }

    private static void PrintParseHelp()
    {
        Console.WriteLine($"{"  <parse> [path]",-35} Parse a .tgs file and outputs errors/success status");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"{"  --daemon",-35} Run in daemon mode");
        Console.WriteLine($"{"  --json",-35} Output in JSON format");
        Console.WriteLine($"{"  --pretty",-35} Pretty print JSON output");
    }
    #endregion
}