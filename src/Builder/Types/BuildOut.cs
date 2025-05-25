
using System.Text.Json.Serialization;

namespace typegen.Builder.Types;

public class BuildOut(
    string lang,
    string outputPath
)
{
    [JsonPropertyName("lang")] 
    public string Lang { get; } = lang;

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = outputPath;

    public void Deconstruct(out string lang, out string outputPath)
    {
        lang = Lang;
        outputPath = OutputPath;
    }
}