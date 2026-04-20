using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeekToolDownloader.Models;
using GeekToolDownloader.Services;

namespace GeekToolDownloader.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly IDownloadService _downloadService;
        private readonly IInstallationService _installationService;
        private readonly IEnvironmentService _environmentService;

        public ObservableCollection<ToolItemViewModel> Tools { get; } = new ObservableCollection<ToolItemViewModel>();

        [ObservableProperty]
        private bool _isSettingsOpen;

        [ObservableProperty]
        private int _selectedCount;

        [ObservableProperty]
        private bool _isDownloadingAny;

        [ObservableProperty]
        private string _actionButtonText = "开始安装部署";

        [ObservableProperty]
        private string _selectAllButtonText = "一键全选";

        [ObservableProperty]
        private string _pathInputText = string.Empty;

        public SettingsViewModel Settings { get; }

        private CancellationTokenSource? _cts;
        private int _downloadProcessGate;

        public MainViewModel(
            IConfigurationService configService,
            IDownloadService downloadService,
            IInstallationService installationService,
            IEnvironmentService environmentService,
            SettingsViewModel settings)
        {
            _configService = configService;
            _downloadService = downloadService;
            _installationService = installationService;
            _environmentService = environmentService;
            Settings = settings;
            
            _ = LoadToolsAsync();
        }

        [RelayCommand]
        private async Task LoadToolsAsync()
        {
            var list = await _configService.LoadToolListAsync();
            Tools.Clear();
            foreach (var item in list)
            {
                var vm = new ToolItemViewModel(item);
                vm.StatusText = "检测中...";
                Tools.Add(vm);
            }

            RefreshSelectionSummary();
            SelectAllToolsCommand.NotifyCanExecuteChanged();

            var checkConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            var semaphore = new SemaphoreSlim(checkConcurrency);
            var checks = Tools.Select(async vm =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var installed = await Task.Run(() => _installationService.IsInstalled(vm.Model));
                    RunOnUi(() => vm.UpdateStatus(installed));
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(checks);
            }
            finally
            {
                semaphore.Dispose();
                RefreshSelectionSummary();
                SelectAllToolsCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void ToggleToolSelection(ToolItemViewModel tool)
        {
            if (tool.IsDownloading || tool.IsInstalled) return;

            tool.IsSelected = !tool.IsSelected;
            RefreshSelectionSummary();
        }

        [RelayCommand(CanExecute = nameof(CanSelectAllTools))]
        private void SelectAllTools()
        {
            var selectableTools = Tools.Where(t => !t.IsInstalled && !t.IsDownloading).ToList();
            if (!selectableTools.Any()) return;

            var shouldSelectAll = selectableTools.Any(t => !t.IsSelected);
            foreach (var tool in selectableTools)
            {
                tool.IsSelected = shouldSelectAll;
            }

            RefreshSelectionSummary();
        }

        [RelayCommand]
        private void OpenSettings() => IsSettingsOpen = true;

        [RelayCommand]
        private void CloseSettings() => IsSettingsOpen = false;

        [RelayCommand]
        private void ShowAddPathDialog()
        {
            PathInputText = string.Empty;
            (Application.Current.MainWindow as MainWindow)?.ShowAddPathDialog();
        }

        [RelayCommand]
        private void CloseAddPathDialog()
        {
            (Application.Current.MainWindow as MainWindow)?.HideAddPathDialog();
        }

        [RelayCommand]
        private async Task ConfirmAddPathAsync()
        {
            var path = PathInputText?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                (Application.Current.MainWindow as MainWindow)?.ShowTrayNotification("提示", "请输入有效的路径");
                return;
            }

            (Application.Current.MainWindow as MainWindow)?.HideAddPathDialog();

            var success = await Task.Run(() => _environmentService.AddToPath(path!));
            
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (success)
            {
                mainWindow?.ShowTrayNotification("环境变量已更新", $"路径已添加：{path}");
            }
            else
            {
                mainWindow?.ShowTrayNotification("操作失败", "添加环境变量失败，请检查权限");
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartDownloadProcess))]
        private async Task StartDownloadProcessAsync()
        {
            if (Interlocked.CompareExchange(ref _downloadProcessGate, 1, 0) != 0) return;

            var selectedTools = new List<ToolItemViewModel>();
            string? runTempDir = null;
            try
            {
                selectedTools = Tools.Where(t => t.IsSelected).ToList();
                if (!selectedTools.Any()) return;

                IsDownloadingAny = true;
                ActionButtonText = "安装同步中...";
                _cts = new CancellationTokenSource();

                var maxConcurrency = Math.Max(1, _configService.Config.MaxConcurrentDownloads);
                var channel = Channel.CreateUnbounded<ToolItemViewModel>();

                foreach (var tool in selectedTools)
                {
                    tool.IsSelected = false;
                    tool.IsDownloading = true;
                    tool.ResetRuntimeState();
                    tool.TransferText = "已下载 0 B · 0 B/s";
                    tool.StatusText = "等待中...";
                    channel.Writer.TryWrite(tool);
                }
                channel.Writer.Complete();

                var tempRootDir = Path.Combine(Path.GetTempPath(), "GeekToolDownloader");
                CleanupStaleTempDirectories(tempRootDir, TimeSpan.FromHours(12));
                runTempDir = Path.Combine(tempRootDir, $"run_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(runTempDir);

                var tasks = new Task[maxConcurrency];
                for (int i = 0; i < maxConcurrency; i++)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        while (await channel.Reader.WaitToReadAsync(_cts.Token))
                        {
                            while (channel.Reader.TryRead(out var tool))
                            {
                                try
                                {
                                    await ProcessToolAsync(tool, runTempDir);
                                }
                                catch
                                {
                                    UpdateToolStatus(tool, "失败");
                                }
                            }
                        }
                    });
                }

                await Task.WhenAll(tasks);
                ActionButtonText = "部署完成";
                await Task.Delay(2000);
                ActionButtonText = "开始安装部署";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsDownloadingAny = false;
                Interlocked.Exchange(ref _downloadProcessGate, 0);

                foreach (var tool in selectedTools)
                {
                    tool.IsDownloading = false;
                }

                RefreshSelectionSummary();
                SelectAllToolsCommand.NotifyCanExecuteChanged();

                if (!string.IsNullOrEmpty(runTempDir))
                {
                    TryDeleteDirectory(runTempDir!);
                }
            }
        }

        private async Task ProcessToolAsync(ToolItemViewModel tool, string tempDir)
        {
            UpdateToolStatus(tool, "下载中...");
            var safeName = InstallationService.GetSafeFileName(tool.Model.Name);
            var filePath = Path.Combine(tempDir, safeName + GetDownloadExtension(tool.Model));

            bool success = false;
            int retryCount = 0;
            while (!success && retryCount < 3)
            {
                try
                {
                    var progress = new Progress<DownloadProgressInfo>(p => 
                    {
                        UpdateToolProgress(tool, p);
                    });

                    if (_cts != null)
                    {
                        await _downloadService.DownloadFileAsync(tool.Model.Url, filePath, progress, _cts.Token);

                        if (_configService.Config.VerifyHash)
                        {
                            UpdateToolStatus(tool, "校验中...");
                            var isValid = await _downloadService.VerifyHashAsync(filePath, tool.Model.Hash, _cts.Token);
                            if (!isValid)
                            {
                                File.Delete(filePath);
                                throw new Exception("Hash mismatch");
                            }
                        }
                    }

                    success = true;
                }
                catch
                {
                    retryCount++;
                    if (retryCount < 3)
                        await Task.Delay((int)Math.Pow(2, retryCount - 1) * 1000); // 1s, 2s, 4s
                }
            }

            if (!success)
            {
                UpdateToolStatus(tool, "下载失败");
                return;
            }

            UpdateToolStatus(tool, "安装中...");
            var installed = await _installationService.InstallAsync(tool.Model, filePath);

            if (installed)
            {
                RunOnUi(() => 
                {
                    tool.StatusText = "完成";
                    tool.IsInstalled = true;
                    tool.IsSelected = false;
                    if (tool.IsArchivePackage)
                    {
                        var folderPath = GetArchiveInstallDirectory(tool);
                        tool.OpenFolderPath = folderPath;
                        tool.IsOpenFolderVisible = Directory.Exists(folderPath);
                    }
                });
            }
            else
            {
                UpdateToolStatus(tool, "安装失败");
            }

            RunOnUi(() =>
            {
                RefreshSelectionSummary();
                SelectAllToolsCommand.NotifyCanExecuteChanged();
            });
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Settings?.Dispose();

            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private bool CanStartDownloadProcess()
        {
            return SelectedCount > 0 && !IsDownloadingAny;
        }

        private bool CanSelectAllTools()
        {
            return !IsDownloadingAny && Tools.Any(t => !t.IsInstalled && !t.IsDownloading);
        }

        [RelayCommand]
        private void OpenToolFolder(ToolItemViewModel tool)
        {
            if (tool == null || !tool.IsArchivePackage || string.IsNullOrWhiteSpace(tool.OpenFolderPath)) return;
            if (!Directory.Exists(tool.OpenFolderPath)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{tool.OpenFolderPath}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        partial void OnSelectedCountChanged(int value)
        {
            StartDownloadProcessCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsDownloadingAnyChanged(bool value)
        {
            StartDownloadProcessCommand.NotifyCanExecuteChanged();
            SelectAllToolsCommand.NotifyCanExecuteChanged();
        }

        private static string GetDownloadExtension(ToolItemModel model)
        {
            if (Uri.TryCreate(model.Url, UriKind.Absolute, out var uri))
            {
                var absolutePath = uri.AbsolutePath;
                if (absolutePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    return ".tar.gz";
                }

                var extFromUri = Path.GetExtension(absolutePath);
                if (!string.IsNullOrWhiteSpace(extFromUri) && extFromUri.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    return extFromUri;
                }
            }

            return model.Type switch
            {
                ToolType.Msi => ".msi",
                ToolType.Msix => ".msix",
                ToolType.MsixBundle => ".msixbundle",
                ToolType.Zip => ".zip",
                ToolType.Tar => ".tar",
                ToolType.SevenZip => ".7z",
                ToolType.ExeInstaller => ".exe",
                ToolType.ExeStandalone => ".exe",
                _ => ".bin"
            };
        }

        private void RefreshSelectionSummary()
        {
            int selectedCount = 0;
            int selectableCount = 0;
            int selectedSelectableCount = 0;

            foreach (var tool in Tools)
            {
                if (tool.IsSelected) selectedCount++;

                if (!tool.IsInstalled && !tool.IsDownloading)
                {
                    selectableCount++;
                    if (tool.IsSelected) selectedSelectableCount++;
                }
            }

            SelectedCount = selectedCount;
            SelectAllButtonText = selectableCount > 0 && selectedSelectableCount == selectableCount
                ? "取消全选"
                : "一键全选";
        }

        private static void UpdateToolStatus(ToolItemViewModel tool, string status)
        {
            RunOnUi(() => tool.StatusText = status);
        }

        private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

        private static void UpdateToolProgress(ToolItemViewModel tool, DownloadProgressInfo progress)
        {
            RunOnUi(() =>
            {
                tool.TransferText = $"已下载 {FormatSize(progress.DownloadedBytes)} · {FormatSize(progress.BytesPerSecond)}/s";
            });
        }

        private static string FormatSize(double bytes)
        {
            if (bytes <= 0) return "0 B";

            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < ByteUnits.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {ByteUnits[unit]}";
        }

        private static string GetArchiveInstallDirectory(ToolItemViewModel tool) =>
            InstallationService.GetArchiveInstallDirectory(tool.Model);

        private static readonly Dispatcher _uiDispatcher = Application.Current.Dispatcher;

        private static void RunOnUi(Action action)
        {
            if (_uiDispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _uiDispatcher.InvokeAsync(action, DispatcherPriority.Normal);
            }
        }

        private static void CleanupStaleTempDirectories(string rootDir, TimeSpan maxAge)
        {
            try
            {
                Directory.CreateDirectory(rootDir);
                var cutoff = DateTime.UtcNow - maxAge;
                foreach (var dir in Directory.EnumerateDirectories(rootDir, "run_*"))
                {
                    try
                    {
                        var created = Directory.GetCreationTimeUtc(dir);
                        if (created < cutoff)
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
