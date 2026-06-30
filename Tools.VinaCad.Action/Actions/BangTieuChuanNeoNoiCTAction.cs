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
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCad.Action.Actions
{
    public class BangTieuChuanNeoNoiCTAction
    {
        private BangTieuChuanNeoNoiCTWindow _bangTieuChuanNeoNoiCTView;
        private BangTraNeoNoiCTVM _bangTieuChuanNeoNoiCTVM;
       
        public BangTieuChuanNeoNoiCTAction()
        {
            _bangTieuChuanNeoNoiCTVM = new BangTraNeoNoiCTVM();
            _bangTieuChuanNeoNoiCTView = new BangTieuChuanNeoNoiCTWindow() { DataContext = _bangTieuChuanNeoNoiCTVM };
        }
        public void Execute()
        {
            try
            {
                 _bangTieuChuanNeoNoiCTVM.ImageNeoNoiVN = ImageHelper.GetImagePath(StringDefinition.TenBangNeoNoiVN);
                _bangTieuChuanNeoNoiCTVM.ImageNeoNoiChauAu = ImageHelper.GetImagePath(StringDefinition.TenBangNeoNoiChauAu);
                _bangTieuChuanNeoNoiCTView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BangTieuChuanNeoNoiCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
