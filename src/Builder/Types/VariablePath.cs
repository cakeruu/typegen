namespace typegen.Builder.Types;

public class VariablePath(string name, string path)
{
    public string Name { get; init; } = name;
    public string Path { get; init; } = path;
}