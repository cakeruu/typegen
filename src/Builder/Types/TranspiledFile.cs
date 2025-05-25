namespace typegen.Builder.Types;

public class TranspiledFile(string path, string content)
{
    public string Path { get; init; } = path;
    public string Content { get; init; } = content;
}