namespace typegen.Builder.Types;

public class TgEnum(string name, List<string> values, string pathToOutput)
{
    public string Name { get; init; } = name;
    public List<string> Values { get; init; } = values;
    public string PathToOutput { get; init; } = pathToOutput;
}