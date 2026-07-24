$ErrorActionPreference = 'Stop'
Set-Location 'D:\Jamil\Projects\QuotaDock'
$stray = 'D:\Jamil\Projects\QuotaDock\downloads\latest\QuotaDock-0.1.1-win-x64-portable'
$guard = 'D:\Jamil\Projects\QuotaDock\downloads\latest\'
if ((Test-Path -LiteralPath $stray) -and $stray.StartsWith($guard, [System.StringComparison]::OrdinalIgnoreCase)) {
    Get-ChildItem -LiteralPath $stray -Recurse -Force -File | ForEach-Object {
        try { $_.Attributes = 'Normal' } catch { }
    }
    Remove-Item -LiteralPath $stray -Recurse -Force
    Write-Output 'Removed stray extracted folder.'
}
Write-Output '----- downloads/latest now -----'
Get-ChildItem 'downloads\latest' | Select-Object Mode, Name
git add -A 2>$null
Write-Output '----- staged status -----'
git status --short
