using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Tools.Model;

namespace Tools.View.UI
{
    public partial class ExportCsvProgressWindow : Window
    {
        private readonly Action _cancelAction;
        private bool _canClose;
        private bool _cancelRequested;
        private bool _cancellationEnabled = true;

        public ExportCsvProgressWindow(string filePath, Action cancelAction)
        {
            InitializeComponent();
            _cancelAction = cancelAction ?? throw new ArgumentNullException(nameof(cancelAction));
            txtFileName.Text = $"エクスポート中のファイル: {Path.GetFileName(filePath)}";
            txtFileName.ToolTip = filePath;
            Closing += Window_Closing;
        }

        public void UpdateProgress(EntityPropertyExportProgress progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(progress));
                return;
            }

            txtStage.Text = progress.Stage;
            progressBar.IsIndeterminate = progress.IsIndeterminate;
            progressBar.Value = progress.Percentage;
            txtPercentage.Text = progress.IsIndeterminate
                ? "処理中..."
                : $"{progress.Percentage:N1}%";

            if (progress.TotalObjectCount > 0 &&
                progress.ProcessedObjectCount >= progress.TotalObjectCount)
            {
                _cancellationEnabled = false;
            }

            Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        public void CloseAfterExport()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(CloseAfterExport);
                return;
            }

            _canClose = true;
            Close();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_canClose)
            {
                e.Cancel = true;
                RequestCancellation();
            }
        }

        private void RequestCancellation()
        {
            if (_cancelRequested || !_cancellationEnabled)
            {
                return;
            }

            _cancelRequested = true;
            txtStage.Text = "CSV エクスポートをキャンセルしています...";
            txtPercentage.Text = "キャンセル中...";
            _cancelAction();
        }
    }
}
