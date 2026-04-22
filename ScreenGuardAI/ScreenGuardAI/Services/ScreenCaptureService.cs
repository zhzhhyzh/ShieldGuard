using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ScreenGuardAI.Helpers;

namespace ScreenGuardAI.Services;

/// <summary>
/// Screen capture service optimized for capturing video meetings (Zoom, Google Meet, Teams, etc.)
/// Uses Graphics.CopyFromScreen which captures the DWM-composited desktop,
/// including GPU-accelerated / hardware-overlay windows.
/// </summary>
public class ScreenCaptureService
{
    /// <summary>
    /// Captures the primary screen. Works with all meeting apps because DWM composites
    /// the final desktop output, including GPU-rendered Zoom/Meet/Teams windows.
    /// </summary>
    public Bitmap CapturePrimaryScreen()
    {
        // SetProcessDPIAware is called once in App startup to get correct pixel dimensions.
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        if (width <= 0 || height <= 0)
        {
            width = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            height = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(0, 0, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    /// <summary>
    /// Captures the entire virtual screen (all monitors).
    /// </summary>
    public Bitmap CaptureFullScreen()
    {
        int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    /// <summary>
    /// Captures the currently active (foreground) window.
    /// Uses DwmGetWindowAttribute for accurate DPI-aware bounds.
    /// </summary>
    public Bitmap? CaptureActiveWindow()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        // DwmGetWindowAttribute with DWMWA_EXTENDED_FRAME_BOUNDS gives accurate bounds
        // that respect DPI scaling and exclude window shadows.
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT rect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
                return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0) return null;

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    /// <summary>
    /// Converts a Bitmap to a Base64-encoded JPEG string.
    /// Uses JPEG for much smaller payload (important for API latency during live interviews).
    /// Resizes large screenshots to stay within API limits and reduce cost.
    /// </summary>
    public string BitmapToBase64(Bitmap bitmap)
    {
        var resized = ResizeIfNeeded(bitmap, maxDimension: 2048);
        using var ms = new MemoryStream();

        // Use JPEG quality 85 - good balance of size vs clarity for text readability
        var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        if (jpegEncoder != null)
        {
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
            resized.Save(ms, jpegEncoder, encoderParams);
        }
        else
        {
            resized.Save(ms, ImageFormat.Jpeg);
        }

        if (!ReferenceEquals(resized, bitmap))
            resized.Dispose();

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Converts a Bitmap to a PNG byte array.
    /// </summary>
    public byte[] BitmapToBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Resizes proportionally if either dimension exceeds maxDimension.
    /// Keeps text readable while reducing API payload size.
    /// </summary>
    private Bitmap ResizeIfNeeded(Bitmap original, int maxDimension)
    {
        if (original.Width <= maxDimension && original.Height <= maxDimension)
            return original;

        double scale = Math.Min(
            (double)maxDimension / original.Width,
            (double)maxDimension / original.Height);

        int newWidth = (int)(original.Width * scale);
        int newHeight = (int)(original.Height * scale);

        var resized = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.DrawImage(original, 0, 0, newWidth, newHeight);
        }
        return resized;
    }

    private ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        return ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == format.Guid);
    }
}
