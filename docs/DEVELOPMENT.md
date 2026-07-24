# Development guide

## Repository layout

```text
QuotaDock/
├─ src/                 Application, core, connectors, and infrastructure
├─ tests/               xUnit unit and integration tests
├─ e2e/                 Native Windows UI Automation tests
├─ docs/                Architecture, security, and working specifications
├─ packaging/           MSIX manifest and packaging inputs
├─ scripts/             Local build and release scripts
├─ downloads/
│  ├─ latest/           Only the current portable ZIP and unsigned MSIX
│  └─ archive/          Older release binaries grouped by version
└─ artifacts/           Generated build, coverage, and test output; ignored by Git
```

## Portable package layout

The portable ZIP is assembled with a clean top level so users do not see a folder
full of DLLs:

```text
QuotaDock-<version>-win-x64/
├─ QuotaDock.exe     Native launcher (src/QuotaDock.Launcher/launcher.c)
└─ app/              The self-contained WinUI 3 app and all runtime files
```

A self-contained WinUI 3 app resolves its native runtime DLLs from the directory
of the running executable, so `QuotaDock.App.exe` and its DLLs cannot be split.
`QuotaDock.exe` is a tiny launcher that starts `app\QuotaDock.App.exe`. The build
also trims unused WinUI framework language folders, keeping only `en-us`, because
the app UI is English-only. Windows resolves `.mui` resources from a folder named
after the language beside the owning DLL, so these language folders cannot be
merged into one without breaking localization; trimming is the safe reduction.

### Building the launcher

`scripts/build-release.ps1` copies a prebuilt launcher from
`packaging/launcher/QuotaDock.exe`. Rebuild it after changing the launcher source:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-launcher.ps1
```

This requires the MSVC C++ build tools (`cl.exe`).

## Prerequisites

- Windows 10 version 1809 or later, or Windows 11
- .NET 10 SDK
- Python 3.11+ for native UI tests

## Restore, build, and test

```powershell
dotnet restore QuotaDock.slnx -p:Platform=x64
dotnet build QuotaDock.slnx -c Release
dotnet test tests/QuotaDock.Core.Tests/QuotaDock.Core.Tests.csproj -c Release
dotnet test tests/QuotaDock.Connectors.Tests/QuotaDock.Connectors.Tests.csproj -c Release
dotnet test tests/QuotaDock.Infrastructure.Tests/QuotaDock.Infrastructure.Tests.csproj -c Release
dotnet format QuotaDock.slnx --verify-no-changes --no-restore
```

## Native UI tests

Install `e2e/requirements.txt`, set `APP_PATH` to a built `QuotaDock.App.exe`, then run:

```powershell
pytest -c e2e/pytest.ini e2e/tests
```

Tests launch with `--e2e` and isolate QuotaDock's application-data folders. They must not use real provider secrets.

## Release build

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

Temporary publish and MSIX staging files go to ignored `artifacts/release-work`. Final downloads go to `downloads/latest`. When a newer version is built, the script moves the previous latest downloads into `downloads/archive/<version>`.

The MSIX is unsigned in the alpha channel. Never commit certificates, private keys, SQLite databases, credentials, cookies, raw dashboard pages, or local test output.
