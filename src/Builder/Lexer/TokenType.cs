namespace typegen.Builder.Lexer;

public enum TokenType
{
    // Literals
    Identifier,
    String,
    Path,

    // Keywords
    Import,
    From,
    Create,
    Schema,
    Enum,

    // Operators and Punctuation
    LeftBrace,      // {
    RightBrace,     // }
    LeftParen,      // (
    RightParen,     // )
    LeftAngleBracket,      // <
    RightAngleBracket,     // >
    Semicolon,      // ;
    Colon,          // :
    Comma,          // ,
    QuestionMark,       // ?
    Ampersand,      // &
    Equals,         // =
    Slash,          // /
    Plus,           // +

    // Special
    Comment,
    Whitespace,
    Newline,
    EOF,

    // Error
    Unknown
}