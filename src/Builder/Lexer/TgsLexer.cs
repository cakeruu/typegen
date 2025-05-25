using System.Text;

namespace typegen.Builder.Lexer;

/// <summary>
/// Lexical analyzer for the TGS (TypeGen Schema) language.
/// Converts source text into a stream of tokens for the parser to consume.
/// 
/// The lexer handles:
/// - Keywords
/// - Identifiers and paths
/// - Operators and punctuation
/// - String literals
/// - Comments (// and /* */)
/// - Proper error reporting with line/column positions
/// </summary>
public class TgsLexer(string source)
{
    private readonly string _source = source;
    private int _position = 0;      // Current position in source
    private int _line = 1;          // Current line number (1-based)
    private int _column = 1;        // Current column number (1-based)
    private readonly List<Token> _tokens = [];

    /// <summary>
    /// Maps string literals to their corresponding keyword token types.
    /// Used to distinguish keywords from regular identifiers.
    /// </summary>
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "import", TokenType.Import },
        { "from", TokenType.From },
        { "create", TokenType.Create },
        { "schema", TokenType.Schema },
        { "enum", TokenType.Enum }
    };

    /// <summary>
    /// Main tokenization method. Scans the entire source text and returns
    /// a list of tokens with an EOF token at the end.
    /// </summary>
    /// <returns>List of tokens representing the source code</returns>
    public List<Token> Tokenize()
    {
        while (!IsAtEnd())
        {
            ScanToken();
        }

        AddToken(TokenType.EOF, "");
        return _tokens;
    }

    /// <summary>
    /// Scans a single token from the current position.
    /// This is the heart of the lexer - it determines what type of token
    /// we're looking at based on the current character.
    /// </summary>
    private void ScanToken()
    {
        var start = _position;
        var startLine = _line;
        var startColumn = _column;

        var c = Advance();

        switch (c)
        {
            // Skip whitespace
            case ' ':
            case '\r':
            case '\t':
                break;

            // Newlines are significant for error reporting
            case '\n':
                AddToken(TokenType.Newline, "\n", startLine, startColumn, start);
                _line++;
                _column = 1;
                break;

            // Single-character tokens
            case '{':
                AddToken(TokenType.LeftBrace, "{", startLine, startColumn, start);
                break;
            case '}':
                AddToken(TokenType.RightBrace, "}", startLine, startColumn, start);
                break;
            case '(':
                AddToken(TokenType.LeftParen, "(", startLine, startColumn, start);
                break;
            case ')':
                AddToken(TokenType.RightParen, ")", startLine, startColumn, start);
                break;
            case '<':
                AddToken(TokenType.LeftAngleBracket, "<", startLine, startColumn, start);
                break;
            case '>':
                AddToken(TokenType.RightAngleBracket, ">", startLine, startColumn, start);
                break;
            case ';':
                AddToken(TokenType.Semicolon, ";", startLine, startColumn, start);
                break;
            case ':':
                AddToken(TokenType.Colon, ":", startLine, startColumn, start);
                break;
            case ',':
                AddToken(TokenType.Comma, ",", startLine, startColumn, start);
                break;
            case '?':
                AddToken(TokenType.QuestionMark, "?", startLine, startColumn, start);
                break;
            case '&':
                AddToken(TokenType.Ampersand, "&", startLine, startColumn, start);
                break;
            case '=':
                AddToken(TokenType.Equals, "=", startLine, startColumn, start);
                break;
            case '+':
                AddToken(TokenType.Plus, "+", startLine, startColumn, start);
                break;

            // Forward slash: could be division, comment, or path
            case '/':
                if (Match('/'))
                {
                    // Line comment: // to end of line
                    ScanComment(startLine, startColumn, start);
                }
                else if (Match('*'))
                {
                    // Block comment: /* ... */
                    ScanBlockComment(startLine, startColumn, start);
                }
                else if (IsPathStart())
                {
                    // Path literal starting with /
                    Back(); // Go back to include the /
                    ScanPath(startLine, startColumn, start);
                }
                else
                {
                    // Just a standalone slash
                    AddToken(TokenType.Slash, "/", startLine, startColumn, start);
                }
                break;

            // String literals (both " and ')
            case '"':
            case '\'':
                ScanString(c, startLine, startColumn, start);
                break;

            default:
                if (IsAlpha(c))
                {
                    // Identifier or keyword
                    ScanIdentifier(startLine, startColumn, start);
                }
                else if (c == '/' || IsPathChar(c))
                {
                    // Path-like sequence
                    ScanPath(startLine, startColumn, start);
                }
                else
                {
                    // Unknown character
                    AddToken(TokenType.Unknown, c.ToString(), startLine, startColumn, start);
                }
                break;
        }
    }

    /// <summary>
    /// Lookahead to determine if a '/' starts a path literal.
    /// This prevents '/' from being consumed as a standalone token when it's
    /// actually part of a path
    /// </summary>
    /// <returns>True if this is the start of a path</returns>
    private bool IsPathStart()
    {
        // Save current position for restoration
        var saved = _position;
        var savedLine = _line;
        var savedColumn = _column;

        // Look ahead to see if this could be a path
        while (!IsAtEnd() && (IsAlphaNumeric(Peek()) || Peek() == '/' || Peek() == '_' || Peek() == '-'))
        {
            Advance();
        }

        // Check if we end with path-terminating characters
        var isPath = !IsAtEnd() && (Peek() == ';' || Peek() == '>' || char.IsWhiteSpace(Peek()));

        // Restore position
        _position = saved;
        _line = savedLine;
        _column = savedColumn;

        return isPath;
    }

    /// <summary>
    /// Scans a line comment (// to end of line).
    /// Includes the entire comment text in the token value.
    /// </summary>
    private void ScanComment(int startLine, int startColumn, int start)
    {
        var content = new StringBuilder("//");

        while (Peek() != '\n' && !IsAtEnd())
        {
            content.Append(Advance());
        }

        AddToken(TokenType.Comment, content.ToString(), startLine, startColumn, start);
    }

    /// <summary>
    /// Scans a block comment (/* ... */).
    /// Handles multi-line comments and tracks line numbers correctly.
    /// </summary>
    private void ScanBlockComment(int startLine, int startColumn, int start)
    {
        var content = new StringBuilder("/*");

        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                content.Append(Advance()); // *
                content.Append(Advance()); // /
                break;
            }

            if (Peek() == '\n')
            {
                _line++;
                _column = 0; // Will be incremented by Advance()
            }

            content.Append(Advance());
        }

        AddToken(TokenType.Comment, content.ToString(), startLine, startColumn, start);
    }

    /// <summary>
    /// Scans a string literal enclosed in quotes.
    /// Supports both single and double quotes.
    /// Handles multi-line strings and tracks line numbers.
    /// </summary>
    /// <param name="quote">The quote character that started the string</param>
    private void ScanString(char quote, int startLine, int startColumn, int start)
    {
        var content = new StringBuilder();

        while (Peek() != quote && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 0; // Will be incremented by Advance()
            }
            content.Append(Advance());
        }

        if (IsAtEnd())
        {
            throw new Exception($"Unterminated string at line {startLine}, column {startColumn}");
        }

        // Consume closing quote
        Advance();

        AddToken(TokenType.String, content.ToString(), startLine, startColumn, start);
    }

    /// <summary>
    /// Scans an identifier or keyword.
    /// Reads alphanumeric characters and underscores, then checks
    /// if the result is a keyword or regular identifier.
    /// </summary>
    private void ScanIdentifier(int startLine, int startColumn, int start)
    {
        var content = new StringBuilder();
        content.Append(_source[start]);

        while (IsAlphaNumeric(Peek()) || Peek() == '_')
        {
            content.Append(Advance());
        }

        var text = content.ToString();
        var type = Keywords.GetValueOrDefault(text, TokenType.Identifier);
        AddToken(type, text, startLine, startColumn, start);
    }

    /// <summary>
    /// Scans a path literal like "/Customers/Requests" or "/enumsDir".
    /// Only handles absolute paths - relative paths are not allowed.
    /// </summary>
    private void ScanPath(int startLine, int startColumn, int start)
    {
        var content = new StringBuilder();

        // Check if this is a relative path (doesn't start with /) - not allowed
        if (_source[start] != '/')
        {
            throw new Exception($"Relative paths are not allowed. Use absolute paths starting with '/'. Error at line {startLine}, column {startColumn}");
        }

        // Handle starting /
        content.Append('/');
        if (_position == start)
            Advance();

        // Scan remaining path characters
        while (!IsAtEnd() && IsPathChar(Peek()))
        {
            content.Append(Advance());
        }

        AddToken(TokenType.Path, content.ToString(), startLine, startColumn, start);
    }

    #region Character Manipulation and Lookahead

    /// <summary>
    /// Advances to the next character and returns the current character.
    /// Updates column position tracking.
    /// </summary>
    /// <returns>The character that was just consumed</returns>
    private char Advance()
    {
        _column++;
        return _source[_position++];
    }

    /// <summary>
    /// Moves back one character. Used when we need to "unread" a character.
    /// </summary>
    private void Back()
    {
        if (_position > 0)
        {
            _position--;
            _column--;
        }
    }

    /// <summary>
    /// Checks if the current character matches the expected character.
    /// If it matches, consumes it and returns true.
    /// </summary>
    /// <param name="expected">Character to match</param>
    /// <returns>True if the character matched and was consumed</returns>
    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_position] != expected) return false;

        _position++;
        _column++;
        return true;
    }

    /// <summary>
    /// Looks at the current character without consuming it.
    /// </summary>
    /// <returns>Current character, or '\0' if at end</returns>
    private char Peek()
    {
        if (IsAtEnd()) return '\0';
        return _source[_position];
    }

    /// <summary>
    /// Looks at the next character without consuming it.
    /// </summary>
    /// <returns>Next character, or '\0' if at or past end</returns>
    private char PeekNext()
    {
        if (_position + 1 >= _source.Length) return '\0';
        return _source[_position + 1];
    }

    /// <summary>
    /// Checks if we've reached the end of the source text.
    /// </summary>
    /// <returns>True if at end of source</returns>
    private bool IsAtEnd()
    {
        return _position >= _source.Length;
    }

    #endregion

    #region Character Classification

    /// <summary>
    /// Checks if a character is alphabetic (a-z, A-Z).
    /// </summary>
    private static bool IsAlpha(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    /// <summary>
    /// Checks if a character is a digit (0-9).
    /// </summary>
    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    /// <summary>
    /// Checks if a character is alphanumeric.
    /// </summary>
    private static bool IsAlphaNumeric(char c)
    {
        return IsAlpha(c) || IsDigit(c);
    }

    /// <summary>
    /// Checks if a character is valid in a path literal.
    /// Includes alphanumeric, forward slash, underscore, hyphen, and dot.
    /// </summary>
    private static bool IsPathChar(char c)
    {
        return IsAlphaNumeric(c) || c == '/' || c == '_' || c == '-' || c == '.';
    }

    #endregion

    #region Token Creation

    /// <summary>
    /// Convenience method to add a token using current position information.
    /// </summary>
    private void AddToken(TokenType type, string value)
    {
        AddToken(type, value, _line, _column - value.Length, _position - value.Length);
    }

    /// <summary>
    /// Creates and adds a token to the token list with precise position information.
    /// </summary>
    /// <param name="type">Type of the token</param>
    /// <param name="value">Text content of the token</param>
    /// <param name="line">Line number where token starts</param>
    /// <param name="column">Column number where token starts</param>
    /// <param name="position">Character position in source where token starts</param>
    private void AddToken(TokenType type, string value, int line, int column, int position)
    {
        _tokens.Add(new Token(type, value, line, column, position));
    }

    #endregion
}