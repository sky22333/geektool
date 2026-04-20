using CommunityToolkit.Mvvm.ComponentModel;
using GeekToolDownloader.Models;

namespace GeekToolDownloader.ViewModels
{
    public partial class ToolItemViewModel : ObservableObject
    {
        private readonly ToolItemModel _model;
        public ToolItemModel Model => _model;

        public ToolItemViewModel(ToolItemModel model)
        {
            _model = model;
        }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private string _transferText = string.Empty;

        [ObservableProperty]
        private bool _isOpenFolderVisible;

        [ObservableProperty]
        private string _openFolderPath = string.Empty;

        [ObservableProperty]
        private string _statusText = "未安装";

        [ObservableProperty]
        private bool _isInstalled;

        public void UpdateStatus(bool installed)
        {
            IsInstalled = installed;
            StatusText = installed ? "已安装" : "未安装";
            if (installed)
            {
                TransferText = string.Empty;
            }
        }

        public bool IsArchivePackage =>
            Model.Type == ToolType.Zip || Model.Type == ToolType.Tar || Model.Type == ToolType.SevenZip;

        public void ResetRuntimeState()
        {
            TransferText = string.Empty;
            IsOpenFolderVisible = false;
            OpenFolderPath = string.Empty;
        }
    }
}
