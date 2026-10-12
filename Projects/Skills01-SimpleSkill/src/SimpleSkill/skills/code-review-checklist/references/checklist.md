# C# Code Review Checklist

- **Async naming and safety** - async methods end in `Async`, return `Task`/`Task<T>` (never
  `async void` except for event handlers), and every awaitable is actually awaited.
- **Exception handling** - no empty `catch` blocks; caught exceptions are logged, rethrown, or
  otherwise acted on, never silently swallowed.
- **Nullability** - reference types that can be null are annotated `?`, and callers check before
  dereferencing.
- **Naming conventions** - types and public members use PascalCase; locals and parameters use
  camelCase.
- **Single responsibility** - each method does one clearly describable thing; extract a helper if
  a method mixes unrelated concerns.
