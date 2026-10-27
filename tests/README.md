# Tests

Test projects live under `tests/`. Each test project targets the component it covers and references the corresponding production project.

## Conventions
- Framework: **xUnit** + **Moq** (per architecture doc).
- Naming: `<Component>.Tests` (e.g. `ScadaCommon.Tests`).
- Location: `tests/<Component>.Tests/`.
- Run all: `dotnet test tests/ScadaCommon.Tests/ScadaCommon.Tests.csproj`.

## Adding a new test project
```bash
mkdir -p tests/MyComponent.Tests
cd tests/MyComponent.Tests
dotnet new xunit
dotnet add reference ../../ScadaCommon/ScadaCommon/ScadaCommon.csproj
dotnet add package Moq
```
