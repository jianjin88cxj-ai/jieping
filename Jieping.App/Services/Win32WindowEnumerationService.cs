using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Jieping.App.Models;

namespace Jieping.App.Services;

public sealed class Win32WindowEnumerationService : IWindowEnumerationService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private static readonly nint ShellWindow = GetShellWindow();

    public IReadOnlyList<DesktopWindowInfo> GetVisibleWindows()
    {
        var windows = new List<DesktopWindowInfo>();
        var currentProcessId = Environment.ProcessId;

        EnumWindows((handle, _) =>
        {
            if (TryCreateWindowInfo(handle, currentProcessId, out var info))
            {
                windows.Add(info);
            }

            return true;
        }, nint.Zero);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public DesktopWindowInfo ResolveWindow(nint handle)
    {
        if (handle == nint.Zero || !IsWindow(handle))
        {
            throw new InvalidOperationException("Selected window was closed.");
        }

        if (IsIconic(handle))
        {
            throw new InvalidOperationException("Selected window is minimized.");
        }

        if (!IsWindowVisible(handle) || IsCloakedWindow(handle))
        {
            throw new InvalidOperationException("Selected window is no longer visible.");
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Selected window no longer has a visible title.");
        }

        var bounds = GetWindowBounds(handle);
        if (bounds.Width < 16 || bounds.Height < 16)
        {
            throw new InvalidOperationException("Selected window is too small to record.");
        }

        return new DesktopWindowInfo(handle, title, GetProcessName(handle), bounds);
    }

    private static bool TryCreateWindowInfo(nint handle, int currentProcessId, out DesktopWindowInfo info)
    {
        info = default!;

        if (handle == nint.Zero ||
            handle == ShellWindow ||
            !IsWindowVisible(handle) ||
            IsIconic(handle) ||
            IsCloakedWindow(handle) ||
            IsToolWindow(handle))
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var processId);
        if (processId == currentProcessId)
        {
            return false;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var bounds = GetWindowBounds(handle);
        if (bounds.Width < 16 || bounds.Height < 16)
        {
            return false;
        }

        info = new DesktopWindowInfo(handle, title, GetProcessName(processId), bounds);
        return true;
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static CaptureRegion GetWindowBounds(nint handle)
    {
        NativeRect rect;

        if (DwmGetWindowAttribute(
                handle,
                DwmwaExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            if (!GetWindowRect(handle, out rect))
            {
                throw new InvalidOperationException("Selected window bounds are unavailable.");
            }
        }

        return new CaptureRegion(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    private static bool IsToolWindow(nint handle)
    {
        return (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExToolWindow) == WsExToolWindow;
    }

    private static bool IsCloakedWindow(nint handle)
    {
        return DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static string GetProcessName(nint handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        return GetProcessName(processId);
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(nint handle, out int processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint handle, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out NativeRect rect,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out int value,
        int attributeSize);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
