using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace QuotaDock.App;

/// <summary>
/// Applies Windows 11 visual polish to QuotaDock windows: true rounded window
/// corners via DWM and the Mica material backdrop for depth. Falls back
/// gracefully on older builds where these APIs are unavailable.
/// </summary>
internal static class WindowStyleHelper
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWcpRound = 2;
    private const int GwlStyle = -16;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsThickFrame = 0x00040000;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Rounds the window corners and optionally applies the Mica backdrop.
    /// Call after <c>InitializeComponent()</c> in each window's constructor.
    /// </summary>
    public static void Apply(Window window, bool useMica = true)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        ApplyRoundedCorners(hwnd);
        if (useMica)
        {
            TryApplyMica(window);
        }
    }

    private static void ApplyRoundedCorners(nint hwnd)
    {
        try
        {
            var style = GetWindowLong(hwnd, GwlStyle);
            // DWM rounded corners require WS_THICKFRAME. If the window was
            // created as WS_POPUP (e.g. by SetBorderAndTitleBar(false,false)),
            // strip WS_POPUP and add WS_THICKFRAME so DWM cooperates.
            style &= ~WsPopup;
            style |= WsThickFrame;
            SetWindowLong(hwnd, GwlStyle, style);
            SetWindowPos(hwnd, 0, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

            var preference = DwmWcpRound;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Older Windows 10 builds don't support this attribute; the window
            // simply keeps square corners.
        }
    }

    private static void TryApplyMica(Window window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica requires Windows 11 22H2+; the solid canvas color remains.
        }
    }
}
