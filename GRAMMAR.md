# TGS (Typegen Schema) Grammar

TGS is a schema definition language that acts as a middleman between different programming languages, allowing you to generate code and share types across them. This document describes its syntax and grammar rules.

## Basic Structure

A TGS file consists of four main parts:
1. Import Declarations (optional)
2. Directory Declarations  
3. Schema Declarations
4. Enum Declarations

## Comments

```ts
// Single line comment

/* Multi-line
   comment */
```

## Import Declarations

Import declarations allow you to reference schemas and enums from other TGS files.

### Syntax
```ts
import { SchemaName1, SchemaName2, EnumName } from "./other-file.tgs";
```

### Rules
- Must be at the top of the file (before any other declarations)
- Import path must be a string literal
- Must end with semicolon
- Can import multiple items separated by commas
- Import names must match exactly with exported schemas/enums
- Imported items can be used in schemas as types or inheritance targets
- Circular imports are not allowed

### Examples
```ts
import { User, Customer } from "./users.tgs";
import { OrderStatus } from "./enums.tgs";

// Now you can use User, Customer, and OrderStatus in this file
create schema Order(
    Customer: Customer;
    User: User;
    Status: OrderStatus;
);
```

## Directory Declarations

Directory declarations define paths that will be used for schema and enum output locations.

### Syntax
```ts
variableName = /absolute/path;
variableName = existingVariable + /relative/path;
```

### Rules
- Must end with semicolon
- Path must start with /
- Can reference previously declared variables using +
- Cannot reference variables declared after the current line
- Cannot reference undefined variables
- Cannot be empty (e.g., `dir = ;`)
- `rootPath` is a special variable that cannot be used in schema declarations

## Schema Declarations

Schemas define the structure of types that will be generated.

### Basic Syntax
```ts
create schema SchemaName<outputDir>(
    property1: Type;
    property2: Type?;  // Optional property
);
```

### Inheritance
```ts
create schema DerivedSchema<outputDir> & BaseSchema(
    // Additional properties...
);
```

### Rules
- Schema names must be unique within the file and across imported schemas
- Cannot inherit from itself
- Can inherit from schemas defined in the same file or imported schemas
- Properties must end with semicolon [ ; ]
- Last property must also have a semicolon
- Output directory is optional
- Output directory must be a defined directory variable (except rootPath)
- Empty schemas are allowed but will show a warning
- Can use imported enums as property types

### Example
```ts
rootPath = /Users;
// Directory declarations
responsesDir = /Responses;
requestsDir = /Requests;

// Output directory not specified, will default to rootPath
create schema BaseEntity(
    Id: Uid;
    CreatedAt: DateTime;
    UpdatedAt: DateTime;
);

create schema UserResponse<responsesDir> & BaseEntity(
    Name: string;
    Email: string?;
);

create schema CreateUserRequest<requestsDir>(
    Name: string;
    Email: string;
);
```

## Enum Declarations

Enums define a set of named constants that will be generated as union types or enums in target languages.

### Syntax
```ts
create enum EnumName<outputDir>(
    Value1,
    Value2,
    Value3
);
```

### Rules
- Enum names must be unique within the file and across imported enums
- Values must be valid identifiers
- Values are separated by commas
- Trailing comma after last value is optional
- Empty enums are not allowed
- Output directory is optional
- Output directory must be a defined directory variable (except rootPath)
- Enum values cannot contain spaces or special characters

### Example
```ts
enumsDir = /Enums;

create enum CustomerStatus<enumsDir>(
    Active,
    Inactive,
    Pending,
    Suspended
);

create enum OrderType(
    Standard,
    Express,
    Overnight
);
```

## Types

### Built-in Types
- `Uid`
- Numeric: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`
- `bool`
- `char`
- `object`
- `string`
- `Date`
- `DateTime`

### Generic Types
- `Array<T>`
- `List<T>`
- `Map<K, V>`
- `Set<T>`
- `Queue<T>`

### Custom Types
- Any schema defined in the current file
- Any enum defined in the current file
- Any imported schema or enum

### Type Rules
- Can use any built-in type
- Can use any previously defined schema as a type
- Can use any defined enum as a type
- Can use imported schemas and enums as types
- Can nest generic types (e.g., `Map<string, List<User>>`)
- Can make any type optional by adding ? suffix
- Generic types must have correct number of type parameters
  - `Map` requires exactly two
  - Others require exactly one

## Property Rules
- Must have format: `name: type;`
- Name must be a valid identifier
- Type must be a valid type (built-in, schema, enum, or generic)
- Must end with semicolon
- Can be marked optional with ? after the type

## Complete Example
```ts
// Import external dependencies
import { BaseEntity } from "./common.tgs";
import { UserRole, AccountStatus } from "./enums.tgs";

// Directory setup
rootPath = /Users;
responsesDir = /Responses;
requestsDir = /Requests;
enumsDir = /Enums;

// Define local enums
create enum UserPreference<enumsDir>(
    EmailNotifications,
    SmsNotifications,
    PushNotifications
);

// Define schemas with inheritance and enum usage
create schema UserResponse<responsesDir> & BaseEntity(
    Name: string;
    Email: string?;
    Role: UserRole;  // From imported enum
    Status: AccountStatus;  // From imported enum
    Preferences: List<UserPreference>;  // Local enum
    Friends: List<UserResponse>;  // Self-reference
    Settings: Map<string, string>;
);

create schema CreateUserRequest<requestsDir>(
    Name: string;
    Email: string;
    Role: UserRole;
    InitialPreferences: List<UserPreference>?;
);
```