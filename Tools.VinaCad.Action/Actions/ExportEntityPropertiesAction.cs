using System.Diagnostics;
using System.IO;
using Prima.VinaCAD.ApplicationServices;
using PrLogTrackingSystem;
using Tools.VinaCad.Helper.Helper;
using Tools.View.UI;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Tools.VinaCad.Action.Actions
{
    public sealed class ExportEntityPropertiesAction
    {
        public void Execute()
        {
            try
            {
                Document? document = Application.DocumentManager.MdiActiveDocument;
                if (document is null)
                {
                    return;
                }

                EntityPropertyCsvExporter exporter = new();

                string drawingName = Path.GetFileNameWithoutExtension(document.Name);
                string defaultFileName = $"{drawingName}_properties.csv";

                SaveFileDialog saveDialog = new()
                {
                    AddExtension = true,
                    CheckPathExists = true,
                    DefaultExt = ".csv",
                    FileName = defaultFileName,
                    Filter = "CSV (*.csv)|*.csv",
                    OverwritePrompt = true,
                    Title = "エンティティプロパティのエクスポート",
                };

                string? drawingDirectory = Path.GetDirectoryName(document.Name);
                if (!string.IsNullOrWhiteSpace(drawingDirectory) && Directory.Exists(drawingDirectory))
                {
                    saveDialog.InitialDirectory = drawingDirectory;
                }

                if (saveDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(saveDialog.FileName))
                {
                    document.Editor.WriteMessage("\nCSV エクスポートをキャンセルしました。");
                    return;
                }

                string filePath = Path.ChangeExtension(saveDialog.FileName, ".csv");
                if (!string.Equals(filePath, saveDialog.FileName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(filePath))
                {
                    MessageBoxResult overwriteResult = MessageBox.Show(
                        $"ファイルは既に存在します:\n{filePath}\n\n上書きしますか？",
                        "CSV エクスポート",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (overwriteResult != MessageBoxResult.Yes)
                    {
                        document.Editor.WriteMessage("\nCSV エクスポートをキャンセルしました。");
                        return;
                    }
                }

                using CancellationTokenSource exportCancellation = new();
                ExportCsvProgressWindow progressWindow = new(
                    filePath,
                    exportCancellation.Cancel);
                try
                {
                    progressWindow.Show();
                    exporter.Export(
                        document.Database,
                        filePath,
                        progressWindow.UpdateProgress,
                        exportCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    document.Editor.WriteMessage("\nCSV エクスポートをキャンセルしました。");
                    return;
                }
                finally
                {
                    progressWindow.CloseAfterExport();
                }

                document.Editor.WriteMessage($"\nCSV をエクスポートしました: {filePath}");
                OpenInFileExplorer(filePath);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ExportEntityPropertiesAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private static void OpenInFileExplorer(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Path.GetFullPath(filePath)}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(OpenInFileExplorer), ex);
            }
        }
    }
}
