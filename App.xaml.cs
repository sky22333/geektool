using System;
using System.Threading;
using System.Windows;
using GeekToolDownloader.Services;
using GeekToolDownloader.ViewModels;

namespace GeekToolDownloader
{
    public partial class App : Application
    {
        public static IConfigurationService ConfigService { get; private set; } = null!;
        public static MainViewModel MainVM { get; private set; } = null!;
        private static Mutex? _singleInstanceMutex;
        private static bool _ownsSingleInstanceMutex;

        public App()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show(e.Exception.ToString(), "UI Thread Exception");
                e.Handled = true;
                Environment.Exit(1);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                MessageBox.Show(e.ExceptionObject?.ToString(), "AppDomain Exception");
                Environment.Exit(1);
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "GeekToolDownloader_SingleInstanceMutex";
            _singleInstanceMutex = new Mutex(false, appName);
            try
            {
                _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                _ownsSingleInstanceMutex = true;
            }

            if (!_ownsSingleInstanceMutex)
            {
                // App is already running
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

            ConfigService = new ConfigurationService();
            var downloadService = new DownloadService();
            var installationService = new InstallationService();
            var settingsVM = new SettingsViewModel(ConfigService);
            
            MainVM = new MainViewModel(ConfigService, downloadService, installationService, settingsVM);

            var mainWindow = new MainWindow
            {
                DataContext = MainVM
            };
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            MainVM?.Dispose();
            MainVM = null!;

            DownloadService.DisposeHttpClient();

            if (_ownsSingleInstanceMutex && _singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { }
                _singleInstanceMutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}
