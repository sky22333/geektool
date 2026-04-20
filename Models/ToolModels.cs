using System;
using System.IO;

namespace GeekToolDownloader.Models
{
    public enum ToolType
    {
        Msi,
        Msix,
        MsixBundle,
        ExeInstaller,
        ExeStandalone,
        Zip,
        Tar,
        SevenZip,
        Other
    }

    public class ToolItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        
        // Optional Fields
        public string Version { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string Args { get; set; } = string.Empty;
        public string TypeOverride { get; set; } = string.Empty;
        public string[] Check { get; set; } = Array.Empty<string>();

        // Auto-inferred properties (Ignored during serialization if needed, though mostly used for binding/logic)
        [Newtonsoft.Json.JsonIgnore]
        public ToolType Type
        {
            get
            {
                var path = Url;
                if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                {
                    path = uri.AbsolutePath;
                }

                if (!string.IsNullOrEmpty(TypeOverride) && Enum.TryParse<ToolType>(TypeOverride, true, out var t)) return t;
                if (path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return ToolType.Msi;
                if (path.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".appx", StringComparison.OrdinalIgnoreCase)) return ToolType.Msix;
                if (path.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase)) return ToolType.MsixBundle;
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ToolType.Zip;
                if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) return ToolType.Tar;
                if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return ToolType.SevenZip;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return ToolType.ExeInstaller;

                // Unknown extension: keep existing default behavior for compatibility.
                return ToolType.ExeInstaller;
            }
        }

        [Newtonsoft.Json.JsonIgnore]
        public string InstallArgs
        {
            get
            {
                if (!string.IsNullOrEmpty(Args)) return Args;
                if (Type == ToolType.Msi) return "/quiet /norestart";
                return "/S"; // Default for EXE installers
            }
        }
    }

    public class AppConfig
    {
        public bool AutoStart { get; set; } = false;
        public int MaxConcurrentDownloads { get; set; } = 5;
        public bool VerifyHash { get; set; } = false;
        public string RemoteListUrl { get; set; } = string.Empty;
        public string Theme { get; set; } = "Auto";
        public bool ProxyEnabled { get; set; } = false;
        public string ProxyUrl { get; set; } = "socks5://127.0.0.1:10808";
    }
}
