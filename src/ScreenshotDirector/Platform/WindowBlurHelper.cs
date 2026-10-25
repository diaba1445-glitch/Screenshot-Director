using System.Runtime.InteropServices;

namespace ScreenshotDirector.Platform;

public static class WindowBlurHelper
{
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>
    /// Applies Windows Acrylic blur or Backdrop blur to the specified window handle.
    /// Falls back safely if the operating system does not support composition attributes.
    /// </summary>
    public static void EnableBlur(IntPtr hwnd, bool isLightMode)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            // 1. Try Windows 11 DWM SystemBackdrop Type (Acrylic = 4)
            int backdropType = 4; // DWMSBT_ACRYLIC
            int darkMode = isLightMode ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
        catch
        {
            // Ignore on Windows 10
        }

        try
        {
            // 2. Set WindowCompositionAttribute Acrylic Blur
            // GradientColor format: AABBGGRR
            // Light: 0x99FFFFFF (subtle translucent frost)
            // Dark:  0x991C1826 (subtle dark smoked frost)
            int gradientColor = isLightMode ? unchecked((int)0x66FFFFFF) : unchecked((int)0x80181524);

            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int structSize = Marshal.SizeOf(accent);
            IntPtr pMem = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(accent, pMem, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = pMem,
                    SizeOfData = structSize
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pMem);
            }
        }
        catch
        {
            // Fallback gracefully: WPF visual glass brushes will render cleanly
        }
    }
}
