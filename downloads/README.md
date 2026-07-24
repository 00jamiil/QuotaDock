# Downloads

- `latest/` contains only the current portable ZIP and unsigned MSIX.
- `archive/<version>/` contains superseded releases.
- `SHA256SUMS.txt` contains checksums for every retained download.

For most users, download the portable ZIP from `latest/`, extract it to a dedicated folder, and run `QuotaDock.exe` in the root of the extracted folder.

## Portable package layout

The extracted portable package is intentionally simple at the top level:

```text
QuotaDock-<version>-win-x64/
├─ QuotaDock.exe     Small launcher you run; starts the app in the app folder
└─ app/              The self-contained app and all of its runtime files
```

`QuotaDock.exe` is a tiny native launcher that starts `app\QuotaDock.App.exe`. A
self-contained WinUI 3 app resolves its runtime DLLs from its own directory, so
every binary lives together under `app\`; the launcher keeps the folder you open
clean instead of showing a wall of DLLs. You can still run `app\QuotaDock.App.exe`
directly if you prefer. The MSIX is unsigned and intended for development or
downstream signing.
