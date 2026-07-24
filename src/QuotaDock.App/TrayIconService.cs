using System.Runtime.InteropServices;

namespace QuotaDock.App;

internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 37;
    private const uint NotifyAdd = 0x00000000;
    private const uint NotifyDelete = 0x00000002;
    private const uint NotifyModify = 0x00000001;
    private const uint NotifyIcon = 0x00000002;
    private const uint NotifyMessage = 0x00000001;
    private const uint NotifyTip = 0x00000004;
    private const uint NotifyInfo = 0x00000010;
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackReturnCommand = 0x0100;
    private const int WindowProcIndex = -4;
    private const int ShowCommand = 1;
    private const int RefreshCommand = 2;
    private const int DetailsCommand = 3;
    private const int ExitCommand = 4;

    private readonly IntPtr window;
    private readonly Action toggle;
    private readonly Action refresh;
    private readonly Action details;
    private readonly Action exit;
    private readonly WindowProcedure procedure;
    private readonly IntPtr previousProcedure;
    private bool disposed;

    public TrayIconService(
        IntPtr window,
        Action toggle,
        Action refresh,
        Action details,
        Action exit)
    {
        this.window = window;
        this.toggle = toggle;
        this.refresh = refresh;
        this.details = details;
        this.exit = exit;
        procedure = WindowProc;
        previousProcedure = SetWindowLongPtr(window, WindowProcIndex, Marshal.GetFunctionPointerForDelegate(procedure));

        var data = CreateData();
        data.Flags = NotifyMessage | NotifyIcon | NotifyTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        data.Tooltip = "QuotaDock — AI usage";
        _ = ShellNotifyIcon(NotifyAdd, ref data);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var data = CreateData();
        _ = ShellNotifyIcon(NotifyDelete, ref data);
        if (previousProcedure != IntPtr.Zero)
        {
            _ = SetWindowLongPtr(window, WindowProcIndex, previousProcedure);
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (disposed)
        {
            return;
        }

        var data = CreateData();
        data.Flags = NotifyInfo;
        data.InfoTitle = title.Length > 63 ? title[..63] : title;
        data.Info = message.Length > 255 ? message[..255] : message;
        data.InfoFlags = 0x00000001;
        _ = ShellNotifyIcon(NotifyModify, ref data);
    }

    private IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == LeftButtonUp)
            {
                toggle();
                return IntPtr.Zero;
            }

            if (mouseMessage == RightButtonUp)
            {
                ShowMenu();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(previousProcedure, handle, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MenuString, ShowCommand, "Show / hide");
            _ = AppendMenu(menu, MenuString, RefreshCommand, "Refresh now");
            _ = AppendMenu(menu, MenuString, DetailsCommand, "Usage details…");
            _ = AppendMenu(menu, MenuSeparator, 0, null);
            _ = AppendMenu(menu, MenuString, ExitCommand, "Quit QuotaDock");
            _ = SetForegroundWindow(window);
            _ = GetCursorPos(out var point);
            var command = TrackPopupMenu(menu, TrackReturnCommand, point.X, point.Y, 0, window, IntPtr.Zero);
            switch (command)
            {
                case ShowCommand:
                    toggle();
                    break;
                case RefreshCommand:
                    refresh();
                    break;
                case DetailsCommand:
                    details();
                    break;
                case ExitCommand:
                    exit();
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = window,
        Id = IconId,
        Tooltip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tooltip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousProcedure,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int itemId, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
