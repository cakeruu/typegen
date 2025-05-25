namespace typegen.Builder.Types;

public class SchemaFile(string path, string? rootPath, List<VariablePath> variablePaths, List<Schema> schemas, List<Import> imports, List<TgEnum> enums)
{
    public string? Path { get; init; } = path;
    public string? RootPath { get; init; } = rootPath;
    public List<VariablePath> VariablePaths { get; init; } = variablePaths;
    public List<Schema> Schemas { get; init; } = schemas;
    public List<TgEnum> Enums { get; init; } = enums;
    public List<Import> Imports { get; init; } = imports;
}