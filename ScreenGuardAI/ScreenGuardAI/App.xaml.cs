using System.Windows;
using ScreenGuardAI.Helpers;

namespace ScreenGuardAI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Enable per-monitor DPI awareness so screen capture gets correct pixel dimensions
        // on high-DPI displays (common with modern monitors used in meetings).
        try
        {
            NativeMethods.SetProcessDpiAwareness(NativeMethods.PROCESS_PER_MONITOR_DPI_AWARE);
        }
        catch
        {
            // Fallback for older Windows versions
            try { NativeMethods.SetProcessDPIAware(); } catch { }
        }

        base.OnStartup(e);
    }
}
