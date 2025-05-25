using typegen.Builder.Types;

namespace typegen.Builder.Langs;

public abstract class Lang<T> : ILang
{
    protected Dictionary<string, T> TypeTranslations { get; private set; } = null!;

    protected Lang()
    {
        Initialize();
    }

    private void Initialize()
    {
        TypeTranslations = SetTypeTranslations();
    }

    protected abstract Dictionary<string, T> SetTypeTranslations();

    public abstract List<TranspiledFile> TranspileFiles(List<SchemaFile> schemaFiles, string outputPath);
}