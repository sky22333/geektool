using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GeekToolDownloader.Helpers
{
    public static class WindowComposition
    {
        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4, // Win10 1803+
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static readonly int _osBuildNumber = Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 10 ? Environment.OSVersion.Version.Build : 0;

        public static void SetDarkTheme(Window window, bool isDark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (_osBuildNumber >= 17763)
            {
                int attribute = _osBuildNumber >= 18985 ? 20 : 19; 
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, attribute, ref useImmersiveDarkMode, sizeof(int));
            }

            if (_osBuildNumber >= 22000)
            {
                int dwmwaBorderColor = 34; 
                int colorValue = isDark ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
                DwmSetWindowAttribute(hwnd, dwmwaBorderColor, ref colorValue, sizeof(int));
            }
        }

        public static void EnableAcrylic(Window window, bool isEnabled, Color fallbackColor)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (_osBuildNumber >= 22000)
            {
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

                if (_osBuildNumber >= 22621)
                {
                    int acrylic = isEnabled ? 3 : 1; // 3: Acrylic, 1: Auto (default)
                    DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int));
                }
                else
                {
                    int mica = isEnabled ? 1 : 0;
                    DwmSetWindowAttribute(hwnd, 1029, ref mica, sizeof(int));
                }
            }
            else
            {
                ApplyLegacyAcrylic(hwnd, isEnabled, fallbackColor);
            }
        }

        private static void ApplyLegacyAcrylic(IntPtr hwnd, bool isEnabled, Color fallbackColor)
        {
            var policy = new AccentPolicy
            {
                AccentState = isEnabled 
                    ? (IsWindows10OrGreater(17134) ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND : AccentState.ACCENT_ENABLE_BLURBEHIND)
                    : AccentState.ACCENT_DISABLED,
                GradientColor = (fallbackColor.A << 24) | (fallbackColor.B << 16) | (fallbackColor.G << 8) | fallbackColor.R
            };

            var sizeOfPolicy = Marshal.SizeOf(policy);
            var policyPtr = Marshal.AllocHGlobal(sizeOfPolicy);
            Marshal.StructureToPtr(policy, policyPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = sizeOfPolicy,
                Data = policyPtr
            };

            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(policyPtr);
        }

        private static bool IsWindows10OrGreater(int buildNumber)
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version.Major >= 10 && os.Version.Build >= buildNumber;
        }
    }
}
