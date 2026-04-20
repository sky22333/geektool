using System;
using System.Windows;

namespace GeekToolDownloader.Helpers
{
    public static class ThemeManager
    {
        public static void ApplyTheme(string theme)
        {
            string actualTheme = theme;
            if (theme == "Auto")
            {
                actualTheme = Services.ConfigurationService.GetSystemTheme();
            }

            var uri = new Uri($"pack://application:,,,/GeekToolDownloader;component/Themes/{actualTheme}.xaml");
            var dict = new ResourceDictionary { Source = uri };

            var appDicts = Application.Current.Resources.MergedDictionaries;
            for (int i = appDicts.Count - 1; i >= 0; i--)
            {
                if (appDicts[i].Source != null && 
                   (appDicts[i].Source.OriginalString.EndsWith("Dark.xaml") || 
                    appDicts[i].Source.OriginalString.EndsWith("Light.xaml")))
                {
                    appDicts.RemoveAt(i);
                }
            }

            appDicts.Insert(0, dict);

            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.UpdateAcrylicColor();
            }
        }
    }
}
