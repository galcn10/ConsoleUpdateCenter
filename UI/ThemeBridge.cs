using ConsoleUpdateCenter.Core;
using Playnite.SDK;
using System;
using System.Linq;
using System.Windows.Input;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Datos y comandos que el tema puede usar con
    /// {PluginSettings Plugin=ConsoleUpdateCenter, Path=...}
    /// </summary>
    public class ThemeBridge : ObservableBase
    {
        private int pendingCount;
        private int downloadingCount;
        private bool isUpdating;

        public int PendingCount
        {
            get => pendingCount;
            private set { pendingCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPending)); }
        }

        public int DownloadingCount
        {
            get => downloadingCount;
            private set { downloadingCount = value; OnPropertyChanged(); }
        }

        public bool HasPending => PendingCount > 0;

        public bool IsUpdating
        {
            get => isUpdating;
            private set { isUpdating = value; OnPropertyChanged(); }
        }

        public ICommand OpenDownloadsCommand { get; }
        public ICommand UpdateAllCommand { get; }

        public ThemeBridge(Action openDownloads, Action updateAll)
        {
            OpenDownloadsCommand = new SimpleCommand(openDownloads);
            UpdateAllCommand = new SimpleCommand(updateAll);
        }

        /// <summary>Llamar en el hilo de la interfaz.</summary>
        public void Refresh(UpdateService service)
        {
            var entries = service.Entries;
            PendingCount = entries.Count;
            DownloadingCount = entries.Count(e => e.Status == UpdateStatus.Downloading);
            IsUpdating = service.IsUpdatingAll;
        }
    }
}
