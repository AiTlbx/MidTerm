# C# Code Style Guide

This document describes the C# coding conventions for this project.

---

## Formatting

### Indentation & Spacing
- **Indentation size:** 4 spaces
- **Indentation style:** Spaces, no tabs
- **Spaces around binary operators:** Always add spaces before and after (`+, -, *, /, ==, !=, &&, ||`, etc.)
- **Spaces in control flow:** Add space after `if`, `for`, `foreach`, `while`
- **Spaces after commas:** Always
- **No space before commas/semicolons**

### Line Breaks & Braces
- **Brace style:** Allman style - opening brace on new line
    ```csharp
    if (condition)
    {
        // ...
    }
    ```
- **Always use braces:** Even for single-statement blocks
- **New line before `catch`, `else`, `finally`**

## Naming Conventions

- **Classes, Structs, Enums, Delegates:** `PascalCase`
- **Interfaces:** `IPascalCase` (prefix with `I`)
- **Methods:** `PascalCase`
- **Properties & Events:** `PascalCase`
- **Public/Internal Fields:** `PascalCase`
- **Private/Protected Fields:** `_camelCase` (underscore prefix)
- **Local Variables & Parameters:** `camelCase`
- **Constants:** `PascalCase`

## Language Features & Style

### Modifiers
- **Access modifiers:** Always explicit (`public`, `private`, `protected`, `internal`)
- **Readonly:** Mark fields assigned only in constructor as `readonly`

### `var` Keyword
- Use `var` when type is obvious from right-hand side
- Don't use `var` when it obscures the type

### Null Handling
- Prefer `is null` / `is not null` over `== null` / `!= null`
- Use null-conditional operators (`?.`, `?[]`)
- Use null-coalescing operator (`??`)
- Prefer pattern matching over `as` with null checks

### Expression-Bodied Members
- Use for single-line constructors, accessors, properties
- Avoid for regular methods

### Other Preferences
- Prefer language keywords (`int`, `string`) over framework types (`Int32`, `String`)
- Use object and collection initializers
- Prefer compound assignments (`+=`, `-=`)
- Simplify boolean expressions
- Use string interpolation
- Use index (`^1`) and range (`..`) operators
- Prefer switch expressions over switch statements
- Use discards (`_`) for unused values

## Comments

- **Minimalist approach:** Add comments only when code is structurally or algorithmically complex
- **Self-documenting code:** Choose descriptive names over extensive comments
- **XML docs (`///`):** Use primarily for public APIs and complex functions
- **Inline comments (`//`):** Use sparingly for non-obvious decisions
