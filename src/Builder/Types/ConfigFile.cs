using System.Text.Json.Serialization;

namespace typegen.Builder.Types;

public class ConfigFile(
    string schema,
    BuildOut[] buildOut)
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = schema;

    [JsonPropertyName("build_out")]
    public BuildOut[] BuildOut { get; init; } = buildOut;

    public void Deconstruct(out string schema, out BuildOut[] buildOut)
    {
        schema = Schema;
        buildOut = BuildOut;
    }
}