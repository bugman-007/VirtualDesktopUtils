using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VirtualDesktopUtils.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int PickerHotkeyId = 0xD600;
    private const int MoveHotkeyIdBase = 0xD5E0;
    private const int MoveHotkeyNumpadIdBase = 0xD5F0;
    private const int MaxDesktops = 9;
    private const uint VkSpace = 0x20;

    private nint _windowHandle;
    private HwndSource? _hwndSource;
    private Action? _pickerTriggered;
    private Action<int>? _moveTriggered;
    private bool _pickerRegistered;
    private readonly bool[] _moveRegistered = new bool[MaxDesktops + 1];
    private readonly bool[] _moveNumpadRegistered = new bool[MaxDesktops + 1];
    private bool _hookInstalled;

    public bool RegisterPickerHotkey(nint windowHandle, uint modifiers, uint vk, Action onTriggered)
    {
        UnregisterPickerHotkey();
        EnsureHook(windowHandle);
        _pickerTriggered = onTriggered;
        _pickerRegistered = RegisterHotKey(windowHandle, PickerHotkeyId, modifiers | ModNoRepeat, vk);
        return _pickerRegistered;
    }

    public int RegisterMoveHotkeys(nint windowHandle, uint modifiers, Action<int> onDesktopNumberTriggered)
    {
        UnregisterMoveHotkeys();
        EnsureHook(windowHandle);
        _moveTriggered = onDesktopNumberTriggered;

        var count = 0;
        for (var i = 1; i <= MaxDesktops; i++)
        {
            // Register top row number keys (1-9)
            var vkTopRow = (uint)(0x30 + i);
            _moveRegistered[i] = RegisterHotKey(windowHandle, MoveHotkeyIdBase + i, modifiers | ModNoRepeat, vkTopRow);
            if (_moveRegistered[i]) count++;

            // Register numpad keys (Numpad1-Numpad9)
            var vkNumpad = (uint)(0x60 + i);
            _moveNumpadRegistered[i] = RegisterHotKey(windowHandle, MoveHotkeyNumpadIdBase + i, modifiers | ModNoRepeat, vkNumpad);
            if (_moveNumpadRegistered[i]) count++;
        }

        return count;
    }

    public void UnregisterPickerHotkey()
    {
        if (_pickerRegistered && _windowHandle != nint.Zero)
        {
            UnregisterHotKey(_windowHandle, PickerHotkeyId);
            _pickerRegistered = false;
        }
    }

    public void UnregisterMoveHotkeys()
    {
        for (var i = 1; i <= MaxDesktops; i++)
        {
            // Unregister top row hotkeys
            if (_moveRegistered[i] && _windowHandle != nint.Zero)
            {
                UnregisterHotKey(_windowHandle, MoveHotkeyIdBase + i);
                _moveRegistered[i] = false;
            }

            // Unregister numpad hotkeys
            if (_moveNumpadRegistered[i] && _windowHandle != nint.Zero)
            {
                UnregisterHotKey(_windowHandle, MoveHotkeyNumpadIdBase + i);
                _moveNumpadRegistered[i] = false;
            }
        }
    }

    public void Dispose()
    {
        UnregisterPickerHotkey();
        UnregisterMoveHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private void EnsureHook(nint windowHandle)
    {
        _windowHandle = windowHandle;
        if (_hookInstalled)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(WndProc);
        _hookInstalled = true;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return nint.Zero;
        }

        var id = wParam.ToInt32();

        if (id == PickerHotkeyId)
        {
            _pickerTriggered?.Invoke();
            handled = true;
            return nint.Zero;
        }

        // Check top row number keys
        var moveNumber = id - MoveHotkeyIdBase;
        if (moveNumber is >= 1 and <= MaxDesktops)
        {
            _moveTriggered?.Invoke(moveNumber);
            handled = true;
            return nint.Zero;
        }

        // Check numpad keys
        var moveNumpadNumber = id - MoveHotkeyNumpadIdBase;
        if (moveNumpadNumber is >= 1 and <= MaxDesktops)
        {
            _moveTriggered?.Invoke(moveNumpadNumber);
            handled = true;
        }

        return nint.Zero;
    }

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint DefaultPickerVk = VkSpace;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
}
