# Contributing

Use .NET 10 and keep provider-independent code out of the WinUI project. Add or update a failing test before changing normalization, scheduling, persistence, parsers, or connectors. Connector fixtures must be synthetic or sanitized and must never contain live secrets, cookies, tokens, or raw pages.

Before opening a change:

```powershell
dotnet format QuotaDock.slnx --verify-no-changes
dotnet test tests/QuotaDock.Core.Tests/QuotaDock.Core.Tests.csproj
dotnet test tests/QuotaDock.Connectors.Tests/QuotaDock.Connectors.Tests.csproj
dotnet test tests/QuotaDock.Infrastructure.Tests/QuotaDock.Infrastructure.Tests.csproj
dotnet list package --vulnerable --include-transitive
```

For dashboard changes, add a sanitized visible-text fixture and assert unknown signatures fail with `ConnectionHealth.FormatChanged` and no metrics.
