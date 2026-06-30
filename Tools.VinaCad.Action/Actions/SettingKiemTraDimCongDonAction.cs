using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Tools.AutoCad.Action.Actions;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class SettingKiemTraDimCongDonAction
    {
        private SettingKiemTraDimCongDonVM _settingKiemTraDimCongDonVM;
        private SettingKiemTraDimCongDonWindow _settingKiemTraDimCongDonView;
        private Document _doc;
        private Database _db;
        private Editor _ed;

        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }

        public SettingKiemTraDimCongDonAction()
        {
            _settingKiemTraDimCongDonVM = new SettingKiemTraDimCongDonVM()
            {
                OKCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _settingKiemTraDimCongDonView = new SettingKiemTraDimCongDonWindow() { DataContext = _settingKiemTraDimCongDonVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                LoadSettingKiemTraDimCongDonA();
                _settingKiemTraDimCongDonView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SettingKiemTraDimCongDonAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void OkInvoke()
        {
            try
            {

                XLDCDSetting.LechPhuong = _settingKiemTraDimCongDonVM.LechPhuong;
                XLDCDSetting.LechChanDim = _settingKiemTraDimCongDonVM.LechChanDim;
                XLDCDSetting.IsLeftToRightHorizontal = _settingKiemTraDimCongDonVM.IsLeftToRightHorizontal;
                XLDCDSetting.IsRightToLeftHorizontal = _settingKiemTraDimCongDonVM.IsRightToLeftHorizontal;
                XLDCDSetting.IsTopToBottomVertical = _settingKiemTraDimCongDonVM.IsTopToBottomVertical;
                XLDCDSetting.IsBottomToTopVertical = _settingKiemTraDimCongDonVM.IsBottomToTopVertical;
                XLDCDSetting.IsBottomToTopAligned = _settingKiemTraDimCongDonVM.IsBottomToTopAligned;
                XLDCDSetting.IsTopToBottomAligned = _settingKiemTraDimCongDonVM.IsTopToBottomAligned;
                _settingKiemTraDimCongDonView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void CancelInvoke()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                _settingKiemTraDimCongDonView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SettingKiemTraDimCongDonAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void LoadSettingKiemTraDimCongDonA()
        {
            if (XLDCDSetting.LechPhuong > 0)
            {
                _settingKiemTraDimCongDonVM.LechPhuong = XLDCDSetting.LechPhuong;
            }
            if (XLDCDSetting.LechChanDim > 0)
            {
                _settingKiemTraDimCongDonVM.LechChanDim = XLDCDSetting.LechChanDim;
            }
            if (!XLDCDSetting.IsLeftToRightHorizontal && !XLDCDSetting.IsRightToLeftHorizontal)
                XLDCDSetting.IsLeftToRightHorizontal = true;

            if (!XLDCDSetting.IsTopToBottomVertical && !XLDCDSetting.IsBottomToTopVertical)
                XLDCDSetting.IsBottomToTopVertical  = true;

            if (!XLDCDSetting.IsBottomToTopAligned && !XLDCDSetting.IsBottomToTopAligned)
                XLDCDSetting.IsBottomToTopAligned = true;

            _settingKiemTraDimCongDonVM.IsLeftToRightHorizontal = XLDCDSetting.IsLeftToRightHorizontal;
            _settingKiemTraDimCongDonVM.IsRightToLeftHorizontal = XLDCDSetting.IsRightToLeftHorizontal;
            _settingKiemTraDimCongDonVM.IsTopToBottomVertical = XLDCDSetting.IsTopToBottomVertical;
            _settingKiemTraDimCongDonVM.IsBottomToTopVertical = XLDCDSetting.IsBottomToTopVertical;
            _settingKiemTraDimCongDonVM.IsBottomToTopAligned = XLDCDSetting.IsBottomToTopAligned;
            _settingKiemTraDimCongDonVM.IsTopToBottomAligned = XLDCDSetting.IsTopToBottomAligned;
           
        }
    }
}
