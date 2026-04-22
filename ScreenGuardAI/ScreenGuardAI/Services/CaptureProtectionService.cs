using ScreenGuardAI.Helpers;

namespace ScreenGuardAI.Services;

public class CaptureProtectionService
{
    private bool _isProtected;

    public bool IsProtected => _isProtected;

    /// <summary>
    /// Enables screen capture protection on the specified window.
    /// The window will appear black in screenshots and screen recordings.
    /// Requires Windows 10 version 2004 or later.
    /// </summary>
    public bool EnableProtection(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        bool result = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        _isProtected = result;
        return result;
    }

    /// <summary>
    /// Disables screen capture protection on the specified window.
    /// </summary>
    public bool DisableProtection(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        bool result = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
        if (result) _isProtected = false;
        return result;
    }

    /// <summary>
    /// Toggles capture protection on the specified window.
    /// </summary>
    public bool ToggleProtection(IntPtr hwnd)
    {
        return _isProtected ? DisableProtection(hwnd) : EnableProtection(hwnd);
    }
}
