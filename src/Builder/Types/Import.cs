namespace typegen.Builder.Types;

public class Import
{
    public string Path { get; set; } = string.Empty;
    public List<string> ImportNames { get; set; } = [];
    public List<Schema> Schemas { get; set; } = [];
    public List<TgEnum> Enums { get; set; } = [];
}