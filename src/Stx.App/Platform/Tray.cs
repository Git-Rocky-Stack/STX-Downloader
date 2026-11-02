using System.Runtime.InteropServices;

namespace Stx.App.Platform;

/// <summary>A notification-area icon: click to open, right-click for a small menu. It's only there while tray mode is on.</summary>
public sealed partial class Tray : IDisposable
{
    const int CallbackMessage = 0x0400 + 77;
    const int LeftUp = 0x0202, RightUp = 0x0205;
    const uint Add = 0, Delete = 2, FlagMessage = 1, FlagIcon = 2, FlagTip = 4;
    const uint MenuOpen = 1, MenuQuit = 2;
    const uint ReturnCommand = 0x100, RightButton = 2;
    const nuint SubclassId = 7701;

    readonly IntPtr _window;
    readonly Action _open, _quit;
    readonly SubclassProc _proc;
    bool _shown;

    public Tray(IntPtr window, Action open, Action quit)
    {
        _window = window;
        _open = open;
        _quit = quit;
        _proc = OnMessage;
        SetWindowSubclass(window, _proc, SubclassId, 0);
    }

    public void SetVisible(bool visible)
    {
        if (visible == _shown) return;
        var data = Data();
        if (visible) Shell_NotifyIcon(Add, ref data);
        else Shell_NotifyIcon(Delete, ref data);
        _shown = visible;
    }

    public void Dispose()
    {
        SetVisible(false);
        RemoveWindowSubclass(_window, _proc, SubclassId);
    }

    NotifyIconData Data() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = FlagMessage | FlagIcon | FlagTip,
        CallbackMessage = CallbackMessage,
        Icon = ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0),
        Tip = "STX Downloader",
    };

    IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == CallbackMessage)
        {
            int mouse = (int)lParam & 0xFFFF;
            if (mouse == LeftUp) _open();
            else if (mouse == RightUp) ShowMenu();
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenuW(menu, 0, MenuOpen, "Open STX Downloader");
        AppendMenuW(menu, 0, MenuQuit, "Quit");
        GetCursorPos(out var at);
        SetForegroundWindow(_window);
        uint choice = TrackPopupMenu(menu, ReturnCommand | RightButton, at.X, at.Y, 0, _window, IntPtr.Zero);
        DestroyMenu(menu);
        if (choice == MenuOpen) _open();
        else if (choice == MenuQuit) _quit();
    }

    delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconW")]
    static extern IntPtr ExtractIcon(IntPtr instance, string file, uint index);

    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(IntPtr window, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string text);

    [DllImport("user32.dll")]
    static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
}
