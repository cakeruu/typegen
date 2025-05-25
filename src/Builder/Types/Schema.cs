namespace typegen.Builder.Types;

public class Schema(
    string pathToOutput, 
    string name, 
    List<TgProperty> props, 
    Schema? inheritance = null,
    string? inheritanceName = null)
{
    public string PathToOutput { get; init; } = pathToOutput;
    public string Name { get; init; } = name;
    public List<TgProperty> Props { get; init; } = props;
    public Schema? Inheritance { get; set; } = inheritance;
    public string? InheritanceName { get; init; } = inheritanceName;
}