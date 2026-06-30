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
    public class BangTraDTCTAction
    {
        private BangTraDTCTWindow _bangTraDTCTView;
        private BangTraDTCTWM _bangTraDTCTVM;
        public BangTraDTCTAction()
        {
            _bangTraDTCTVM = new BangTraDTCTWM();
            _bangTraDTCTView = new BangTraDTCTWindow() { DataContext = _bangTraDTCTVM };
        }
        public void Execute()
        {
            try
            {
                _bangTraDTCTVM.ImageDTCT = ImageHelper.GetImagePath(StringDefinition.TenBangDTCT);
                _bangTraDTCTView.ShowDialog();
         
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BangTraDTCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
