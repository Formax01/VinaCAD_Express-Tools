using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class AboutExpressToolAction
    {
        private AboutExpressToolVM _aboutExpressToolVM;
        private AboutExpressToolWindow _aboutExpressToolView;
        
        public AboutExpressToolAction()
        {
            _aboutExpressToolVM = new AboutExpressToolVM()
            {
                HyperlinkCmd = new RelayCommand(HyperlinkInvoke),
                CloseCmd = new RelayCommand(CancelInvoke)
            };
            _aboutExpressToolView = new AboutExpressToolWindow() { DataContext = _aboutExpressToolVM };
        }
        public void Execute()
        {
            try
            {
                _aboutExpressToolVM.CurrentVersion = GetVersion();
                _aboutExpressToolView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(AboutExpressToolAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _aboutExpressToolView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(AboutExpressToolAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void HyperlinkInvoke()
        {
            try
            {
                OpenManualPdf();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(AboutExpressToolAction), ex);
                MessageBox.Show($"Không thể mở file hướng dẫn.\n{ex.Message}", "Lỗi",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }
       
        private void OpenManualPdf()
        {
            try
            {
                string pdfPath = GetManualPdfPath(StringDefinition.PDFAboutTools);

                if (!File.Exists(pdfPath))
                {
                    MessageBox.Show($"Không tìm thấy file hướng dẫn:\n{pdfPath}","Thông báo",MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(AboutExpressToolAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private static string GetManualPdfPath(string pdfFileName)
        {
            string dllFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            DirectoryInfo dir = new DirectoryInfo(dllFolder);

            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Resources")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
                throw new DirectoryNotFoundException("Không tìm thấy thư mục Resources.");

            return Path.Combine(dir.FullName, "Resources", "Templates", pdfFileName);
        }
        private string GetVersion()
        {
            try
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string directoryPath = Path.GetDirectoryName(assemblyPath);

                string dllPath = Path.Combine(directoryPath, "Tools.VinaCad.App.dll");

                if (!File.Exists(dllPath))
                    return "";

                Assembly assembly = Assembly.LoadFrom(dllPath);
                Version version = assembly.GetName().Version;

                return version.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
