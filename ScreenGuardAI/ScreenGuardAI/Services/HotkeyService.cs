using System.Windows;
using System.Windows.Interop;
using ScreenGuardAI.Helpers;

namespace ScreenGuardAI.Services;

public class HotkeyService : IDisposable
{
    private const int HOTKEY_ID_ASK_AI = 9001;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    /// <summary>
    /// Registers the global hotkey (Ctrl+Shift+Q) for the given window.
    /// </summary>
    public bool Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;

        if (_hwnd == IntPtr.Zero) return false;

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        _isRegistered = NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_ASK_AI, modifiers, NativeMethods.VK_Q);

        return _isRegistered;
    }

    /// <summary>
    /// Unregisters the global hotkey.
    /// </summary>
    public void Unregister()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_ASK_AI);
            _isRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_ASK_AI)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
