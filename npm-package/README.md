<div align="center">
  <img src="./assets/banner.png" alt="Typegen Banner" width="100%" />

  <h1>Typegen</h1>
  <p>A powerful type generation tool that acts as a bridge between different programming languages.</p>

  <div>
    <a href="https://github.com/cakeruu/typegen/blob/main/LICENSE.md">
      <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
    </a>
    <a href="https://www.npmjs.com/package/@cakeru/typegen">
      <img src="https://img.shields.io/npm/v/@cakeru/typegen?color=green" alt="npm version">
    </a>
  </div>
</div>

## Overview

Typegen allows you to define your types once using a simple schema definition language (TGS) and generate them for multiple target languages. Write your schemas in `.tgs` files and let Typegen handle the rest.

## Currently Supported Languages
- TypeScript
- C#

## Supported Platforms
- Windows
- macOS
- Linux

**Note on macOS and Linux platforms:** I do not personally own macOS or Linux systems. Therefore, while `make` targets are provided for these platforms, I rely on the community for testing and validation. Please report any issues you encounter.

## Key Features

- **Schema Definitions**: Define complex data structures with inheritance support
- **Enum Support**: Create type-safe enumerations that translate across languages  
- **Import System**: Share types across multiple schema files with a clean import syntax
- **Path Variables**: Organize your generated code with flexible directory structures
- **Generic Types**: Support for arrays, lists, maps, and other generic collections
- **CLI Integration**: Simple commands for project initialization and code generation
- **IDE Support**: VS Code extension with syntax highlighting and validation

## Installation

> **⚠️ Important Update Notice**  
> If you have a version lower than **1.1.4**, please update to the latest version to ensure compatibility and access to the newest features


### CLI Tool
```bash
npm install -g @cakeru/typegen
```

### VS Code Extension For .tgs File Support
1. Open VS Code
2. Go to Extensions
3. Search for "Typegen"
4. Click Install

The extension provides:
- Syntax highlighting
- IntelliSense
- Error detection
- Auto-completion
- Schema/Enum validation
- Formatter

<small><i>You can also compile the extension yourself from the source code in the <a href="https://github.com/cakeruu/typegen-editor-plugins">Typegen editor plugins repository</a>.</i></small>

## Getting Started

### Create a New Project
```bash
typegen create-project my-shared-types
```
This will:
1. Create a new directory `my-shared-types`
2. Initialize a `typegen.config.json` file
3. Create a `.typegen` directory (added to .gitignore)

Or initialize in an existing project <strong><i>(not recommended)</i></strong>:
```bash
typegen init
```

### Configuration

The `typegen.config.json` file defines where your types will be generated:

```json
{
  "$schema": "https://raw.githubusercontent.com/cakeruu/typegen/main/json-schema/typegen-config-schema.json",
  "build_out": [
    {
      "lang": "c#",
      "output_path": "./backend/Types"
    },
    {
      "lang": "typescript",
      "output_path": "./frontend/src/types"
    }
  ]
}
```

<small><i>The config file has built-in autocompletion through JSON schema.</i></small>

### Create Schema Files

Create `.tgs` files in your project to define your types. Typegen supports schemas, enums, and imports:

#### Basic Schema Example
```ts
rootPath = /Users;
responsesDir = /Responses;
requestsDir = /Requests;

create schema User<responsesDir>(
    Id: Uid;
    Name: string;
    Email: string?;
);

create schema UserRequest<requestsDir>(
    Name: string;
    Email: string;
);
```

#### Enums Example
```ts
enumsDir = /Enums;

create enum UserStatus<enumsDir>(
    Active,
    Inactive,
    Pending,
    Suspended
);

create schema User(
    Id: Uid;
    Name: string;
    Status: UserStatus;  // Using the enum as a type
);
```

#### Imports Example
```ts
// users.tgs
import { OrderStatus } from "./orders.tgs";
import { BaseEntity } from "./common.tgs";

create schema User & BaseEntity(
    Name: string;
    Email: string;
    LastOrderStatus: OrderStatus?;  // Using imported enum
);
```

#### Advanced Features
```ts
// Import from other files
import { BaseEntity, Address } from "./common.tgs";
import { OrderStatus, PaymentMethod } from "./enums.tgs";

// Directory variables with concatenation
rootPath = /Commerce;
responseDir = rootPath + /Responses;
enumsDir = rootPath + /Enums;

// Local enums
create enum CustomerType<enumsDir>(
    Individual,
    Business,
    Enterprise
);

// Schema with inheritance and complex types
create schema Customer<responseDir> & BaseEntity(
    Name: string;
    Email: string;
    Type: CustomerType;
    Addresses: List<Address>;  // Imported schema
    OrderHistory: Map<string, OrderStatus>;  // Imported enum
    Preferences: Map<string, Array<PaymentMethod>>;  // Nested generics
);
```

<small><i>See [GRAMMAR.md](./GRAMMAR.md) for detailed TGS language documentation.</i></small> 

### Generate Code
```bash
typegen build
```
This will generate the corresponding types in all configured languages and output directories.

## CLI Commands

| Command | Description |
|---------|-------------|
| <sub>`build`</sub> | <sub>Generate code for all schemas and enums in the current directory. Use the `--help` flag to display the available options</sub> |
| <sub>`parse`</sub> | <sub>Parse a .tgs file and outputs errors/success status. Use the `--help` flag to display the available options</sub> |
| <sub>`init`</sub> | <sub>Initialize Typegen in current directory</sub> |
| <sub>`create-project [name]`</sub> | <sub>Create a new Typegen project</sub> |
| <sub>`--help` or `-h`</sub> | <sub>Display available commands and usage</sub> |


## Project Structure
```
my-shared-types/
├── typegen.config.json
├── .typegen/                 # Build cache (gitignored)
├── common.tgs               # Shared base types
├── users.tgs                # User-related schemas
├── orders.tgs               # Order-related schemas and enums
└── enums.tgs                # Global enumerations
```

## Contributing

Contributions are welcome! Please check out our [Contributing Guide](./CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License - see the [LICENSE.md](./LICENSE.md) file for details.