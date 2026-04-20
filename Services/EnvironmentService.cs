using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GeekToolDownloader.Services
{
    public interface IEnvironmentService
    {
        bool AddToPath(string directory);
    }

    public class EnvironmentService : IEnvironmentService
    {
        private const int HWND_BROADCAST = 0xffff;
        private const uint WM_SETTINGCHANGE = 0x001A;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
            uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        public bool AddToPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            directory = directory.Trim().TrimEnd('\\', '/');

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Environment", writable: true);
                if (key == null) return false;

                var currentPath = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
                var paths = currentPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim().TrimEnd('\\', '/'))
                    .ToList();

                if (paths.Any(p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                paths.Add(directory);
                var newPath = string.Join(";", paths);

                key.SetValue("Path", newPath, RegistryValueKind.ExpandString);

                BroadcastEnvironmentChange();

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return TryElevateAndAddToPath(directory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add to PATH: {ex.Message}");
                return false;
            }
        }

        private static bool TryElevateAndAddToPath(string directory)
        {
            try
            {
                var escapedDir = directory.Replace("\"", "\\\"");
                var psCommand = $@"
$path = [Environment]::GetEnvironmentVariable('Path', 'User')
$paths = $path -split ';' | Where-Object {{ $_ -ne '' }} | ForEach-Object {{ $_.TrimEnd('\', '/') }}
$newDir = '{escapedDir}'.TrimEnd('\', '/')
if ($paths -notcontains $newDir) {{
    $newPath = ($paths + $newDir) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    exit 0
}} else {{
    exit 0
}}
";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit(30000);
                
                if (process.ExitCode == 0)
                {
                    BroadcastEnvironmentChange();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void BroadcastEnvironmentChange()
        {
            try
            {
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "Environment",
                    2,
                    5000,
                    out _);
            }
            catch
            {
            }
        }
    }
}
