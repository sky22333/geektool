using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GeekToolDownloader.Helpers;
using GeekToolDownloader.ViewModels;

namespace GeekToolDownloader
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExitRequested;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            
            if (App.MainVM != null)
            {
                App.MainVM.PropertyChanged += MainVM_PropertyChanged;
            }

            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            UnsubscribeEvents();
            TrayIcon?.Dispose();
            base.OnClosed(e);
        }

        private void UnsubscribeEvents()
        {
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            }
            catch { }

            if (App.MainVM != null)
            {
                try
                {
                    App.MainVM.PropertyChanged -= MainVM_PropertyChanged;
                }
                catch { }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExitRequested)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            base.OnClosing(e);
        }

        private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
            {
                if (App.MainVM?.Settings?.Config?.Theme == "Auto")
                {
                    Dispatcher.Invoke(() =>
                    {
                        ThemeManager.ApplyTheme("Auto");
                    });
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(App.MainVM.Settings.Config.Theme ?? "Auto");
            UpdateAcrylicColor();
            GeekToolDownloader.Services.DownloadService.UpdateHttpClient(App.MainVM.Settings.Config);

            var os = Environment.OSVersion;
            if (os.Platform == PlatformID.Win32NT && os.Version.Major >= 10 && os.Version.Build >= 22000)
            {
                if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is System.Windows.Shell.WindowChrome chrome)
                {
                    chrome.CornerRadius = new CornerRadius(0);
                }
            }
        }

        public void UpdateAcrylicColor()
        {
            string currentTheme = App.MainVM?.Settings?.Config?.Theme ?? "Auto";
            if (currentTheme == "Auto")
            {
                currentTheme = Services.ConfigurationService.GetSystemTheme();
            }

            bool isDark = currentTheme == "Dark";
            WindowComposition.SetDarkTheme(this, isDark);

            if (Application.Current.Resources["AcrylicColor"] is Color color)
            {
                RootBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgWindow");
                WindowComposition.EnableAcrylic(this, true, color);
            }
        }

        private void MainVM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
            {
                if (App.MainVM.IsSettingsOpen)
                {
                    AnimateView(MainView, MainViewScale, 1, 0.9, 1, 0);
                    SettingsView.IsHitTestVisible = true;
                    AnimateView(SettingsView, SettingsViewScale, 1.1, 1, 0, 1);
                }
                else
                {
                    AnimateView(MainView, MainViewScale, 0.9, 1, 0, 1);
                    SettingsView.IsHitTestVisible = false;
                    AnimateView(SettingsView, SettingsViewScale, 1, 1.1, 1, 0);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.ActionButtonText) && App.MainVM.ActionButtonText == "部署完成")
            {
                TrayIcon.ShowNotification("极客工具下载器", "所有选中的工具已安装部署完成");
            }
        }

        private async void AnimateView(UIElement element, ScaleTransform transform, double fromScale, double toScale, double fromOp, double toOp)
        {
            if (toOp > 0) element.Visibility = Visibility.Visible;

            var duration = new Duration(TimeSpan.FromMilliseconds(400));
            var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            var scaleAnim = new DoubleAnimation(fromScale, toScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var opAnim = new DoubleAnimation(fromOp, toOp, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            
            transform.ScaleX = toScale;
            transform.ScaleY = toScale;
            element.Opacity = toOp;

            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            element.BeginAnimation(UIElement.OpacityProperty, opAnim);

            await System.Threading.Tasks.Task.Delay(400);

            if (element.Opacity == 0)
            {
                element.Visibility = Visibility.Collapsed;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Normal) WindowState = WindowState.Maximized;
                else WindowState = WindowState.Normal;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            ShutdownApplication();
        }

        private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ToolItemViewModel item)
            {
                App.MainVM.ToggleToolSelectionCommand.Execute(item);
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void AutoTheme_Click(object sender, RoutedEventArgs e)
        {
            App.MainVM.Settings.Config.Theme = "Auto";
            App.ConfigService.SaveConfig();
            ThemeManager.ApplyTheme("Auto");
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            App.MainVM.Settings.Config.Theme = "Dark";
            App.ConfigService.SaveConfig();
            ThemeManager.ApplyTheme("Dark");
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            App.MainVM.Settings.Config.Theme = "Light";
            App.ConfigService.SaveConfig();
            ThemeManager.ApplyTheme("Light");
        }

        private void ShutdownApplication()
        {
            if (_isExplicitExitRequested)
            {
                return;
            }

            _isExplicitExitRequested = true;

            UnsubscribeEvents();
            App.MainVM?.Dispose();

            try
            {
                TrayIcon?.Dispose();
            }
            catch { }

            try
            {
                Hide();
                Close();
            }
            catch { }

            Application.Current.Shutdown();

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Environment.Exit(0);
            });
        }

        private void MinimizeToTray()
        {
            WindowState = WindowState.Minimized;
            Hide();
        }

        public void ShowAddPathDialog()
        {
            AddPathDialog.Visibility = Visibility.Visible;
            AddPathDialog.IsHitTestVisible = true;
            
            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var opAnim = new DoubleAnimation(0, 1, duration) 
            { 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } 
            };
            
            AddPathDialog.BeginAnimation(UIElement.OpacityProperty, opAnim);
            
            Dispatcher.InvokeAsync(() =>
            {
                PathInputBox?.Focus();
                PathInputBox?.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        public void HideAddPathDialog()
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(150));
            var opAnim = new DoubleAnimation(1, 0, duration) 
            { 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } 
            };
            
            opAnim.Completed += (s, e) =>
            {
                AddPathDialog.Visibility = Visibility.Collapsed;
                AddPathDialog.IsHitTestVisible = false;
            };
            
            AddPathDialog.BeginAnimation(UIElement.OpacityProperty, opAnim);
        }

    }
}
