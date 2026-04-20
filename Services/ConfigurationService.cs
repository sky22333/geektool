using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GeekToolDownloader.Models;

namespace GeekToolDownloader.Services
{
    public interface IConfigurationService
    {
        AppConfig Config { get; }
        void SaveConfig();
        Task<List<ToolItemModel>> LoadToolListAsync();
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configPath;
        private readonly string _localListPath;
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public AppConfig Config { get; private set; } = new AppConfig();

        public ConfigurationService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDir = Path.Combine(appData, "GeekToolDownloader");
            if (!Directory.Exists(appDir))
                Directory.CreateDirectory(appDir);

            _configPath = Path.Combine(appDir, "config.json");
            _localListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tools.json");
            
            LoadConfig();
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    Config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                catch
                {
                    Config = new AppConfig();
                }
            }
            else
            {
                Config = new AppConfig();
                Config.Theme = "Auto";
                SaveConfig();
            }
        }

        public static string GetSystemTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null && key.GetValue("AppsUseLightTheme") is int useLightTheme)
                {
                    return useLightTheme == 1 ? "Light" : "Dark";
                }
            }
            catch { }
            return "Light";
        }

        public void SaveConfig()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { /* Ignore */ }
        }

        public async Task<List<ToolItemModel>> LoadToolListAsync()
        {
            List<ToolItemModel>? list = null;

            if (!string.IsNullOrEmpty(Config.RemoteListUrl))
            {
                try
                {
                    var json = await _httpClient.GetStringAsync(Config.RemoteListUrl);
                    list = JsonConvert.DeserializeObject<List<ToolItemModel>>(json);
                }
                catch { }
            }

            if (list == null && File.Exists(_localListPath))
            {
                try
                {
                    var json = File.ReadAllText(_localListPath);
                    list = JsonConvert.DeserializeObject<List<ToolItemModel>>(json);
                }
                catch { }
            }

            if (list == null)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream("GeekToolDownloader.Assets.tools.json");
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        list = JsonConvert.DeserializeObject<List<ToolItemModel>>(json);
                    }
                }
                catch { }
            }

            return list ?? new List<ToolItemModel>();
        }
    }
}
