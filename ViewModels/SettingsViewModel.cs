using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeekToolDownloader.Models;
using GeekToolDownloader.Services;

namespace GeekToolDownloader.ViewModels
{
    public partial class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly IConfigurationService _configService;
        private CancellationTokenSource? _saveDebounce;

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
            bool success = TrySetAutoStart(value);
            
            if (success)
            {
                _configService.Config.AutoStart = value;
                _configService.SaveConfig();
            }
            else if (value)
            {
                AutoStart = false;
                System.Windows.MessageBox.Show(
                    "无法设置开机自启动。请检查是否有足够的权限。", 
                    "设置失败", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private bool TrySetAutoStart(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (key == null) return false;

                string appName = "GeekToolDownloader";
                
                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) return false;
                    
                    key.SetValue(appName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        partial void OnMaxConcurrentDownloadsChanged(int value)
        {
            var clampedValue = Math.Max(1, Math.Min(20, value));
            if (clampedValue != value)
            {
                MaxConcurrentDownloads = clampedValue;
                return;
            }
            
            _configService.Config.MaxConcurrentDownloads = clampedValue;
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
            DebounceSave();
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
            DebounceProxyUpdate();
        }

        private void DebounceSave()
        {
            _saveDebounce?.Cancel();
            _saveDebounce?.Dispose();
            _saveDebounce = new CancellationTokenSource();
            var token = _saveDebounce.Token;

            _ = Task.Run(async () =>
            {
                await Task.Delay(500, token);
                if (!token.IsCancellationRequested)
                {
                    _configService.SaveConfig();
                }
            }, token);
        }

        private void DebounceProxyUpdate()
        {
            _saveDebounce?.Cancel();
            _saveDebounce?.Dispose();
            _saveDebounce = new CancellationTokenSource();
            var token = _saveDebounce.Token;

            _ = Task.Run(async () =>
            {
                await Task.Delay(500, token);
                if (!token.IsCancellationRequested)
                {
                    _configService.SaveConfig();
                    DownloadService.UpdateHttpClient(_configService.Config);
                }
            }, token);
        }

        public void Dispose()
        {
            _saveDebounce?.Cancel();
            _saveDebounce?.Dispose();
            _saveDebounce = null;
        }
    }
}
