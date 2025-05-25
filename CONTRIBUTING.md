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

## 3. How to contribute to language generators

The language generators convert TGS schemas to target languages. Currently supported: TypeScript, C#.
You can contribute by:

1. Filing bugs on generated code quality.
2. Add support for new target languages (Go, Rust, Python, etc.).
3. Improve existing generators (better naming, imports, etc.).
4. Fix type mapping issues.
5. Work on cross-language feature parity.

### How to add a new language generator

If you want to add support for a new target language:

1. Create a new transpiler class in `Builder/Langs/YourLanguage.cs`.
2. Implement type mappings from TGS types to your language types.
3. Handle schemas, enums, inheritance, and imports.
4. Add the language to the supported languages enum.
5. Test with various TGS files to ensure quality output.

Example structure:
```csharp
public class YourLanguage : Lang<YourLanguageType>
{
    protected override Dictionary<string, YourLanguageType> SetTypeTranslations() 
    {
        // Map TGS types to your language types
    }
    
    public override List<TranspiledFile> TranspileFiles(List<SchemaFile> schemaFiles, string outputPath)
    {
        // Generate code for your language
    }
}
```

### How to work on existing generators

For small improvements to TypeScript or C# generators:

1. Ensure generated code follows language best practices.
2. Add unit tests for your changes.
3. Test with real-world schema files.
4. Update documentation if adding new features.

### Maintain a language generator

Each language generator needs dedicated maintainers who will:

1. Review pull requests for that language.
2. Ensure generated code quality and idioms.
3. Keep up with language ecosystem changes.
4. Maintain feature parity across generators.

## 4. How to contribute to various tools

### CLI Tool (Program.cs)
- Improve command-line interface and user experience
- Add new commands or options
- Better error reporting and help messages

### VS Code Extension
- Contribute to the separate [Typegen VS Code repository](https://github.com/cakeruu/typegen-vscode)
- Improve syntax highlighting, IntelliSense, and validation

### Documentation
- Improve README.md with better examples
- Enhance GRAMMAR.md with clearer explanations
- Add tutorials and guides for common use cases

## Code Style Guidelines

- Use **PascalCase** for public members, **camelCase** for private fields
- Add XML documentation for public APIs
- Follow .NET naming conventions
- Write clear, descriptive variable and method names

## Testing

Create test .tgs files to verify your changes:

```typescript
// test.tgs
import { BaseEntity } from "./common.tgs";

rootPath = /Test;
enumsDir = /Enums;

create enum Status<enumsDir>(
    Active,
    Inactive
);

create schema User & BaseEntity(
    Name: string;
    Email: string?;
    Status: Status;
);
```

## Pull Request Process

1. Create a descriptive branch name: `feature/add-rust-generator` or `fix/parser-inheritance`
2. Write clear commit messages describing what you changed
3. Include tests for new features
4. Update documentation when adding language features
5. Ensure generated code compiles in target languages

## Getting Help

- **Issues**: Report bugs and request features via GitHub Issues
- **Discussions**: Use GitHub Discussions for questions and ideas
- **Discord**: Join community discussions (link in README)

Thank you for contributing to Typegen! Your efforts help make cross-language type sharing easier for developers everywhere. 