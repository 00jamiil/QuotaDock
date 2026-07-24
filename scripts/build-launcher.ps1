[CmdletBinding()]
param(
    [string]$Output = ""
)

# Compiles the tiny native root launcher (QuotaDock.exe) that starts the real
# self-contained WinUI 3 app from the "app" subfolder. Requires the MSVC build
# tools (cl.exe). The compiled launcher is written to packaging\launcher so the
# release script can copy it without a C toolchain being present at release time.

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot "src\QuotaDock.Launcher\launcher.c"
if (-not (Test-Path -LiteralPath $source)) {
    throw "Launcher source not found: $source"
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $projectRoot "packaging\launcher\QuotaDock.exe"
}
$outDir = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Locate vcvars64.bat from any installed Visual Studio / Build Tools edition.
$vcvarsCandidates = Get-ChildItem 'C:\Program Files*\Microsoft Visual Studio' -Recurse -Filter 'vcvars64.bat' -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
$vcvars = $vcvarsCandidates | Select-Object -First 1
if ($null -eq $vcvars) {
    throw "vcvars64.bat was not found. Install the MSVC C++ build tools to build the launcher."
}

$objDir = Join-Path $outDir "obj"
New-Item -ItemType Directory -Path $objDir -Force | Out-Null
$cl = 'cl /nologo /O1 /MT "' + $source + '" /Fe:"' + $Output + '" /Fo:"' + $objDir + '\\" /link /SUBSYSTEM:WINDOWS shlwapi.lib user32.lib'
$full = '"' + $vcvars + '" && ' + $cl
cmd /c $full
if (-not (Test-Path -LiteralPath $Output)) {
    throw "Launcher build failed; $Output was not produced."
}
Remove-Item -LiteralPath $objDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "Launcher built: $Output"
