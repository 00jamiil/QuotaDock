// QuotaDock portable launcher.
//
// Purpose: keep the extracted download folder clean. This tiny native
// executable lives at the root of the portable package and starts the real
// self-contained WinUI 3 app, which resides (together with all of its runtime
// DLLs and localized ".mui" resource folders) in the "app" subfolder.
//
// A self-contained .NET / Windows App SDK app resolves its native runtime
// libraries from the directory of the running executable, so the app binaries
// cannot be separated from QuotaDock.App.exe. Relocating the whole app into a
// subfolder and shipping this launcher at the root is the supported way to
// present a single entry point instead of a wall of DLLs.
//
// Build (static CRT, no external dependencies, no console window):
//   cl /O1 /MT launcher.c /Fe:QuotaDock.exe /link /SUBSYSTEM:WINDOWS shlwapi.lib

#include <windows.h>
#include <shlwapi.h>

// Returns a pointer to the argument tail of the process command line, i.e.
// everything after the launcher's own argv[0] token. The returned pointer is
// inside the buffer owned by the OS command line string.
static LPWSTR GetArgumentTail(void)
{
    LPWSTR cmd = GetCommandLineW();
    if (cmd == NULL)
    {
        return NULL;
    }

    if (*cmd == L'"')
    {
        // Quoted program name: skip to the matching closing quote.
        cmd++;
        while (*cmd && *cmd != L'"')
        {
            cmd++;
        }
        if (*cmd == L'"')
        {
            cmd++;
        }
    }
    else
    {
        while (*cmd && *cmd != L' ' && *cmd != L'\t')
        {
            cmd++;
        }
    }

    while (*cmd == L' ' || *cmd == L'\t')
    {
        cmd++;
    }

    return cmd;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR args, int show)
{
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(previous);
    UNREFERENCED_PARAMETER(args);
    UNREFERENCED_PARAMETER(show);

    wchar_t launcherPath[MAX_PATH];
    DWORD length = GetModuleFileNameW(NULL, launcherPath, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        MessageBoxW(NULL, L"Could not resolve the launcher path.", L"QuotaDock", MB_ICONERROR);
        return 1;
    }

    // launcherPath now becomes the launcher directory.
    PathRemoveFileSpecW(launcherPath);

    wchar_t appDir[MAX_PATH];
    if (PathCombineW(appDir, launcherPath, L"app") == NULL)
    {
        MessageBoxW(NULL, L"Could not resolve the app directory.", L"QuotaDock", MB_ICONERROR);
        return 1;
    }

    wchar_t appExe[MAX_PATH];
    if (PathCombineW(appExe, appDir, L"QuotaDock.App.exe") == NULL)
    {
        MessageBoxW(NULL, L"Could not resolve the application path.", L"QuotaDock", MB_ICONERROR);
        return 1;
    }

    if (!PathFileExistsW(appExe))
    {
        MessageBoxW(
            NULL,
            L"QuotaDock.App.exe was not found in the 'app' folder next to this launcher.",
            L"QuotaDock",
            MB_ICONERROR);
        return 1;
    }

    // Compose: "<appExe>" <original argument tail>
    LPWSTR tail = GetArgumentTail();
    size_t tailLength = (tail != NULL) ? lstrlenW(tail) : 0;
    size_t bufferLength = tailLength + MAX_PATH + 8;
    LPWSTR commandLine = (LPWSTR)HeapAlloc(GetProcessHeap(), 0, bufferLength * sizeof(wchar_t));
    if (commandLine == NULL)
    {
        MessageBoxW(NULL, L"Out of memory.", L"QuotaDock", MB_ICONERROR);
        return 1;
    }

    commandLine[0] = L'\0';
    lstrcatW(commandLine, L"\"");
    lstrcatW(commandLine, appExe);
    lstrcatW(commandLine, L"\"");
    if (tailLength > 0)
    {
        lstrcatW(commandLine, L" ");
        lstrcatW(commandLine, tail);
    }

    STARTUPINFOW startupInfo;
    ZeroMemory(&startupInfo, sizeof(startupInfo));
    startupInfo.cb = sizeof(startupInfo);

    PROCESS_INFORMATION processInfo;
    ZeroMemory(&processInfo, sizeof(processInfo));

    BOOL started = CreateProcessW(
        appExe,
        commandLine,
        NULL,
        NULL,
        FALSE,
        0,
        NULL,
        appDir,     // run with the app folder as the working directory
        &startupInfo,
        &processInfo);

    DWORD exitCode = 0;
    if (!started)
    {
        MessageBoxW(NULL, L"QuotaDock could not be started.", L"QuotaDock", MB_ICONERROR);
        exitCode = 1;
    }
    else
    {
        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
    }

    HeapFree(GetProcessHeap(), 0, commandLine);
    return (int)exitCode;
}
