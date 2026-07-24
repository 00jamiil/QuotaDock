[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.1"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot "src\QuotaDock.App\QuotaDock.App.csproj"
$artifactRoot = Join-Path $projectRoot "artifacts\release-work"
$downloadsRoot = Join-Path $projectRoot "downloads"
$downloadRoot = Join-Path $downloadsRoot "latest"
$archiveRoot = Join-Path $downloadsRoot "archive"
$publishRoot = Join-Path $artifactRoot "QuotaDock-$Version-$Runtime"
$stageRoot = Join-Path $artifactRoot "msix-stage"
$portableRoot = Join-Path $artifactRoot "portable\QuotaDock-$Version-$Runtime"
$launcherExe = Join-Path $projectRoot "packaging\launcher\QuotaDock.exe"
# The app UI is English-only, so only these WinUI framework language folders are
# retained. The remaining locale folders are unused chrome and are trimmed.
$keepLanguages = @('en-us')

function Assert-SafeReleasePath([string]$Path) {
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release artifact directory: $resolvedPath"
    }
}

Assert-SafeReleasePath $publishRoot
Assert-SafeReleasePath $stageRoot
Assert-SafeReleasePath $portableRoot

# A WinUI framework language folder contains only *.mui resource files. This is
# how we distinguish locale folders (safe to trim) from real folders such as
# Microsoft.UI.Xaml (which holds assets and must be kept).
function Get-LanguageFolder([string]$Directory) {
    return Get-ChildItem -LiteralPath $Directory -Directory | Where-Object {
        $_.Name -ne 'Microsoft.UI.Xaml' -and
        -not (Get-ChildItem -LiteralPath $_.FullName -Recurse -File | Where-Object { $_.Extension -ne '.mui' })
    }
}

function Remove-UnwantedLanguage([string]$Directory, [string[]]$Keep) {
    $removed = 0
    foreach ($folder in (Get-LanguageFolder $Directory)) {
        if ($Keep -notcontains $folder.Name) {
            Remove-Item -LiteralPath $folder.FullName -Recurse -Force
            $removed++
        }
    }
    return $removed
}

function Assert-SafeDownloadPath([string]$Path) {
    $resolvedDownloadsRoot = [System.IO.Path]::GetFullPath($downloadsRoot).TrimEnd('\') + '\'
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedDownloadsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the downloads directory: $resolvedPath"
    }
}

Assert-SafeDownloadPath $downloadRoot
Assert-SafeDownloadPath $archiveRoot

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot, $stageRoot, $downloadRoot, $archiveRoot -Force | Out-Null

$currentZipName = "QuotaDock-$Version-$Runtime-portable.zip"
$currentMsixName = "QuotaDock-$Version-$Runtime-unsigned.msix"
$previousDownloads = Get-ChildItem -LiteralPath $downloadRoot -File | Where-Object {
    $_.Name -match '^QuotaDock-(?<version>\d+\.\d+\.\d+)-.+\.(zip|msix)$' -and
    $_.Name -notin @($currentZipName, $currentMsixName)
}
foreach ($download in $previousDownloads) {
    $null = $download.Name -match '^QuotaDock-(?<version>\d+\.\d+\.\d+)-'
    $versionArchive = Join-Path $archiveRoot $Matches.version
    Assert-SafeDownloadPath $versionArchive
    New-Item -ItemType Directory -Path $versionArchive -Force | Out-Null
    Move-Item -LiteralPath $download.FullName -Destination (Join-Path $versionArchive $download.Name) -Force
}

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

# The unpackaged WinUI publish target does not currently copy the app PRI even
# though it copies XBF files. Without it, a portable launch fails during XAML load.
$appPri = Get-ChildItem (Join-Path $projectRoot "src\QuotaDock.App\bin\x64\$Configuration") `
    -Recurse -Filter "QuotaDock.App.pri" |
    Where-Object { $_.FullName -match "\\$Runtime\\QuotaDock\.App\.pri$" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $appPri) {
    throw "QuotaDock.App.pri was not produced by the WinUI build."
}
Copy-Item -LiteralPath $appPri.FullName -Destination $publishRoot -Force

# Assemble the clean portable layout: a tiny native launcher (QuotaDock.exe) at
# the package root that starts the real self-contained app, which lives with all
# of its runtime DLLs and resources under the "app" subfolder. A self-contained
# WinUI 3 app resolves its native runtime from the executable's own directory,
# so the app binaries cannot be split from QuotaDock.App.exe; relocating the
# whole app into "app" and shipping a launcher is the supported way to present a
# single clean entry point instead of a directory full of DLLs.
if (Test-Path -LiteralPath $portableRoot) {
    Remove-Item -LiteralPath $portableRoot -Recurse -Force
}
$portableAppDir = Join-Path $portableRoot "app"
New-Item -ItemType Directory -Path $portableAppDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishRoot "*") -Destination $portableAppDir -Recurse

$portableTrimmed = Remove-UnwantedLanguage $portableAppDir $keepLanguages
Write-Output "Portable: trimmed $portableTrimmed unused language folder(s); kept $($keepLanguages -join ', ')."

if (-not (Test-Path -LiteralPath $launcherExe)) {
    throw "Launcher not found: $launcherExe. Build it first with scripts\build-launcher.ps1."
}
Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $portableRoot "QuotaDock.exe") -Force

$zipPath = Join-Path $downloadRoot $currentZipName
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Copy-Item -Path (Join-Path $publishRoot "*") -Destination $stageRoot -Recurse
# The MSIX is an installed package launched by its manifest, so it keeps the flat
# layout (QuotaDock.App.exe at the package root) but drops the same unused
# language folders for parity and a smaller download.
$msixTrimmed = Remove-UnwantedLanguage $stageRoot $keepLanguages
Write-Output "MSIX: trimmed $msixTrimmed unused language folder(s)."
Copy-Item -LiteralPath (Join-Path $projectRoot "packaging\AppxManifest.xml") -Destination $stageRoot
$assetRoot = Join-Path $stageRoot "Assets"
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null

Add-Type -AssemblyName System.Drawing
function New-QuotaDockLogo([string]$Path, [int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::FromArgb(255, 16, 19, 26))
            $margin = [Math]::Max(2, [int]($Size * 0.16))
            $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 98, 214, 181))
            try {
                $graphics.FillEllipse($accent, $margin, $margin, $Size - (2 * $margin), $Size - (2 * $margin))
            }
            finally {
                $accent.Dispose()
            }
            $inner = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 16, 19, 26))
            try {
                $innerMargin = [Math]::Max(3, [int]($Size * 0.34))
                $graphics.FillEllipse($inner, $innerMargin, $innerMargin, $Size - (2 * $innerMargin), $Size - (2 * $innerMargin))
            }
            finally {
                $inner.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

New-QuotaDockLogo (Join-Path $assetRoot "StoreLogo.png") 50
New-QuotaDockLogo (Join-Path $assetRoot "Square44x44Logo.png") 44
New-QuotaDockLogo (Join-Path $assetRoot "Square150x150Logo.png") 150

$makeAppx = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools") `
    -Recurse -Filter "makeappx.exe" |
    Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "makeappx.exe was not found. Restore the project first to install Windows SDK build tools."
}

$msixPath = Join-Path $downloadRoot $currentMsixName
if (Test-Path -LiteralPath $msixPath) {
    Remove-Item -LiteralPath $msixPath -Force
}
& $makeAppx.FullName pack /d $stageRoot /p $msixPath /o | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "MSIX packaging failed."
}

$checksumPath = Join-Path $downloadsRoot "SHA256SUMS.txt"
$checksumLines = Get-ChildItem -LiteralPath $downloadsRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.zip', '.msix') } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($downloadsRoot, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash  $relative"
    }
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding utf8

Write-Output "Portable: $zipPath"
Write-Output "MSIX:     $msixPath"
Write-Output "The MSIX is unsigned by design for the alpha."
