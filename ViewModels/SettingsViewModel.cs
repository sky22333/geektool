using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeekToolDownloader.Models;
using GeekToolDownloader.Services;

namespace GeekToolDownloader.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigurationService _configService;
        public AppConfig Config => _configService.Config;

        public SettingsViewModel(IConfigurationService configService)
        {
            _configService = configService;
            AutoStart = _configService.Config.AutoStart;
            MaxConcurrentDownloads = _configService.Config.MaxConcurrentDownloads;
            VerifyHash = _configService.Config.VerifyHash;
            RemoteListUrl = _configService.Config.RemoteListUrl ?? string.Empty;
            ProxyEnabled = _configService.Config.ProxyEnabled;
            ProxyUrl = _configService.Config.ProxyUrl ?? string.Empty;
        }

        [ObservableProperty]
        private bool _autoStart;

        [ObservableProperty]
        private int _maxConcurrentDownloads;

        [ObservableProperty]
        private bool _verifyHash;

        [ObservableProperty]
        private string _remoteListUrl = string.Empty;

        [ObservableProperty]
        private bool _proxyEnabled;

        [ObservableProperty]
        private string _proxyUrl = string.Empty;

        partial void OnAutoStartChanged(bool value)
        {
            _configService.Config.AutoStart = value;
            _configService.SaveConfig();

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    string appName = "GeekToolDownloader";
                    if (value)
                    {
                        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(appName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch { }
        }

        partial void OnMaxConcurrentDownloadsChanged(int value)
        {
            _configService.Config.MaxConcurrentDownloads = value;
            _configService.SaveConfig();
        }

        partial void OnVerifyHashChanged(bool value)
        {
            _configService.Config.VerifyHash = value;
            _configService.SaveConfig();
        }

        partial void OnRemoteListUrlChanged(string value)
        {
            _configService.Config.RemoteListUrl = value;
            _configService.SaveConfig();
        }

        partial void OnProxyEnabledChanged(bool value)
        {
            _configService.Config.ProxyEnabled = value;
            _configService.SaveConfig();
            DownloadService.UpdateHttpClient(_configService.Config);
        }

        partial void OnProxyUrlChanged(string value)
        {
            _configService.Config.ProxyUrl = value;
            _configService.SaveConfig();
            DownloadService.UpdateHttpClient(_configService.Config);
        }
    }
}
