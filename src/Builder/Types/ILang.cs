namespace typegen.Builder.Types;

public interface ILang
{
    List<TranspiledFile> TranspileFiles(List<SchemaFile> schemaFiles, string outputPath);
}