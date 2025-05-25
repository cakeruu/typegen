namespace typegen.Builder.Types;

public record TgProperty(
    string Type,
    string Name,
    bool IsNullable,
    int TypeLine = 0,
    int TypeColumn = 0
);