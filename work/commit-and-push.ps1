$ErrorActionPreference = 'Continue'
Set-Location 'D:\Jamil\Projects\QuotaDock'
git add -A 2>$null | Out-Null
Write-Output '----- staged summary -----'
git status --short 2>$null
git commit -m "v0.2.0: automatic Claude usage reader, per-provider tabs, auto-detect, OpenAI-compatible presets" 2>&1 | Out-String | Write-Output
Write-Output '----- push -----'
git push origin main 2>&1 | Out-String | Write-Output
Write-Output '----- last log -----'
git log --oneline -3 2>$null
