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
    public class BangTraNeoCTAction
    {
        private BangTieuChuanNeoCTWindow _bangTieuChuanNeoCTView;
        private BangTraNeoCTVM _bangTraNeoCTVM;
        public BangTraNeoCTAction()
        {
            _bangTraNeoCTVM = new BangTraNeoCTVM();
            _bangTieuChuanNeoCTView = new BangTieuChuanNeoCTWindow() { DataContext = _bangTraNeoCTVM };
        }
        public void Execute()
        {
            try
            {
                _bangTraNeoCTVM.ImageNEO = ImageHelper.GetImagePath(StringDefinition.TenBangNEOCT);
                _bangTieuChuanNeoCTView.ShowDialog();

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BangTraDTCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
