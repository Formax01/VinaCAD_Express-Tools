using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCad.Action.Actions
{
    public class BangTraNoiCTAction
    {
        private BangTieuChuanNoiCTWindow _bangTieuChuanNoiCTView;
        private BangTraNoiCTVM _bangTraNoiCTVM;
        public BangTraNoiCTAction()
        {
            _bangTraNoiCTVM = new BangTraNoiCTVM();
            _bangTieuChuanNoiCTView = new BangTieuChuanNoiCTWindow() { DataContext = _bangTraNoiCTVM };
        }
        public void Execute()
        {
            try
            {
                _bangTraNoiCTVM.ImageNOI = ImageHelper.GetImagePath(StringDefinition.TenBangNOICT);
                _bangTieuChuanNoiCTView.ShowDialog();

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BangTraDTCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
