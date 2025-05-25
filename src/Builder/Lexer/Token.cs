namespace typegen.Builder.Lexer;

public record Token(
    TokenType Type,
    string Value,
    int Line,
    int Column,
    int Position
)
{
    public override string ToString()
    {
        return $"{Type}('{Value}') at {Line}:{Column}";
    }
}