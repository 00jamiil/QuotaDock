using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using QuotaDock.Core.Configuration;
using Windows.Graphics;

namespace QuotaDock.App;

internal static class WindowPlacementService
{
    private const int WidgetLogicalWidth = 360;
    private const int DefaultLogicalHeight = 560;
    private const int LogicalMargin = 16;

    public static WindowPlacement Capture(AppWindow window, IntPtr handle, bool isAlwaysOnTop)
    {
        var dpi = (int)GetDpiForWindow(handle);
        var display = DisplayArea.GetFromWindowId(window.Id, DisplayAreaFallback.Nearest);
        return new WindowPlacement(
            window.Position.X,
            window.Position.Y,
            window.Size.Width,
            window.Size.Height,
            display?.DisplayId.Value.ToString("X"),
            dpi <= 0 ? 96 : dpi,
            isAlwaysOnTop);
    }

    public static void Restore(AppWindow window, IntPtr handle, WindowPlacement placement)
    {
        var currentDpi = (int)GetDpiForWindow(handle);
        if (currentDpi <= 0)
        {
            currentDpi = 96;
        }

        var scale = currentDpi / 96d;
        var width = (int)Math.Round(WidgetLogicalWidth * scale);
        var logicalHeight = placement.Height > 0 && placement.Dpi > 0
            ? placement.Height * 96d / placement.Dpi
            : DefaultLogicalHeight;
        var height = (int)Math.Round(Math.Clamp(logicalHeight, 460d, 760d) * scale);

        DisplayArea? display;
        if (string.IsNullOrWhiteSpace(placement.MonitorId))
        {
            display = DisplayArea.Primary;
        }
        else
        {
            display = DisplayArea.GetFromPoint(
                new PointInt32(placement.X, placement.Y),
                DisplayAreaFallback.Nearest);
        }

        var work = display?.WorkArea ?? new RectInt32(0, 0, 1920, 1080);
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        var physicalMargin = (int)Math.Round(LogicalMargin * scale);
        var x = string.IsNullOrWhiteSpace(placement.MonitorId)
            ? work.X + work.Width - width - physicalMargin
            : Math.Clamp(placement.X, work.X, work.X + work.Width - width);
        var y = string.IsNullOrWhiteSpace(placement.MonitorId)
            ? work.Y + physicalMargin
            : Math.Clamp(placement.Y, work.Y, work.Y + work.Height - height);

        window.MoveAndResize(new RectInt32(x, y, width, height));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
