using System.Text.Json;

namespace typegen.Constants;

public static class Constants
{
    // Config file name
    public const string ConfigFileName = "typegen.config.json";

    // Typegen folder name  
    public const string TypeGenFolderName = ".typegen";

    // Default console color
    public static ConsoleColor DefaultConsoleColor = ConsoleColor.White;

    // Provided path. If a final slash is not provided by the user, it will be added automatically.
    public static string providedPath = "";

    // Command line arguments
    public static string[] Args = [];

    
    // The values of the dictionary have to match the values from the $schema "https://raw.githubusercontent.com/cakeruu/typegen/main/schema/typegen-config-schema.json"
    public static readonly Dictionary<SupportedLanguages, string> SupportedLangs = new()
    {
        { SupportedLanguages.CSharp, "c#" },
        { SupportedLanguages.Typescript, "typescript" }
    };
    
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2
    };
}