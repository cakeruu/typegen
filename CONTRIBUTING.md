# How to contribute to Typegen

> **WARNING⚠️:** Unit tests are not yet implemented. 
> Typegen is still in the early stages of development, it is planned to be implemented in the near future.

The Typegen project consists of

1. The TGS language specification.
2. The Typegen compiler/parser, called typegen.
3. The language generators (TypeScript, C#, etc.)
4. Various tools, such as the CLI and VS Code extension

## 1. How to contribute to the TGS language

The TGS language is the schema definition language specification. You can contribute to the language by:

1. Filing enhancement requests for changes to the language syntax.
2. Offering feedback on existing features, by filing issues.
3. Help working on the language specification in [`GRAMMAR.md`](GRAMMAR.md).
4. Help improving language documentation and examples.
5. Suggest new built-in types or language constructs.

## 2. How to contribute to the Typegen compiler

The Typegen compiler consists of the lexer, parser, and core logic + test suites for testing the compiler.
You can contribute by:

1. File bugs (by far the most important thing).
2. Suggest improved diagnostics / error messages.
3. Refactoring existing code (needs deep understanding of the compiler).
4. Add support for new TGS language features.
5. Improve parsing error recovery and validation.
6. Work on inheritance validation and type checking.

### Development Setup

```bash
git clone https://github.com/cakeruu/typegen.git
cd typegen
dotnet build
dotnet run build  # Test with .tgs files in current directory
```

If you want to build the project for release:

```bash
make # Builds for target current platform
make all # Builds for all supported platforms
make windows # Builds for Windows
make osx-intel # Builds for Intel Macs
make osx-arm # Builds for Apple Silicon Macs
make linux # Builds for most Linux distributions
make linux-arm # Builds for ARM-based Linux
```

### Code Structure
- `Builder/Lexer/` - Tokenization of .tgs files
- `Builder/Parser/` - AST generation and validation
- `Builder/Types/` - Data structures and models
- `Program.cs` - CLI interface

### Editor plugins
- Contribute to the separate [Typegen editor plugins](https://github.com/cakeruu/typegen-editor-plugins)
- Improve syntax highlighting, IntelliSense, and validation 