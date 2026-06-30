using Prima.VinaCAD.ApplicationServices;
using Tools.AutoCad.Modeling;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using System;
using System.Windows;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using PrMVVMCore;
using PrLogTrackingSystem;
using MessageBox = System.Windows.MessageBox;

namespace Tools.AutoCad.Action.Actions
{
    public class SampleAction
    {
        private SampleVM _sampleVM;
        private SampleWindow _sampleView;
        public SampleAction()
        {
            _sampleVM = new SampleVM()
            {
                ShowCmd = new RelayCommand(ShowInvoke)
            };
            _sampleView = new SampleWindow() { DataContext = _sampleVM };
        }

        private void ShowInvoke()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                // Do something
                MessageBox.Show("Hello World!", StringDefinition.TITLE_MESSAGE);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        public void Execute()
        {
            try
            {
                BlockModel blockModel = new BlockModel();
                blockModel.Name = "Pipe";

                _sampleVM.TextSample = blockModel.Name;
                // Do something
                _sampleView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}

