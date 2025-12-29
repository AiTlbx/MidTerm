# Coding Guide

This document describes coding guidelines beyond pure code style.

---

## Dependency Injection

### Preferred Methods
- **Constructor Injection:** For required dependencies
- **Property Injection:** Only for optional dependencies
- **Method Injection:** When dependency is needed only for specific method

### Service Lifetime
- **Transient:** Lightweight, stateless services
- **Scoped:** Services with shared state within a request
- **Singleton:** Application-wide state or resource-intensive services
- **Respect lifetime hierarchy:** Don't inject shorter-lived services into longer-lived ones

## Async Programming

- **Naming:** Async methods end with `Async` suffix
- **Return types:** `Task<T>` for values, `Task` for void
- **Avoid blocking:** Never use `.Result` or `.Wait()` (deadlock risk)
- **ConfigureAwait:** Use `.ConfigureAwait(false)` in library code
- **Parallel operations:** Use `Task.WhenAll` and `Task.WhenAny`

## Error Handling

- **Specific exceptions:** Catch specific exceptions, not generic `Exception`
- **Propagation:** Only catch if you can handle; let others propagate
- **Exception parameter:** Name it `ex`
- **Exception messages:** Provide helpful context, use `nameof()` for parameters
- **Exception filters:** Use `when` clause for conditional catching

## Performance Considerations

- **N+1 Problem:** Use `.Include()` or batch queries
- **Object pooling:** Use `ArrayPool<T>` for frequently allocated buffers
- **StringBuilder:** Use for extensive string concatenation
- **LINQ optimization:** Use early termination, async LINQ for DB

## Unit Testing

### Naming
- **Test classes:** `[Subject]Tests`
- **Test methods:** `[Method]_[Scenario]_[Expectation]`

### Structure (Arrange-Act-Assert)
```csharp
[Fact]
public void Method_Scenario_Expectation()
{
    // Arrange
    var sut = new SystemUnderTest();

    // Act
    var result = sut.Method();

    // Assert
    Assert.Equal(expected, result);
}
```

## SOLID Principles

- **SRP:** One class, one reason to change
- **OCP:** Open for extension, closed for modification
- **LSP:** Derived classes substitutable for base classes
- **ISP:** Many specific interfaces over one general interface
- **DIP:** Depend on abstractions, not concretions
