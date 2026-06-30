using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
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
    public class BangTraBeTongAction
    {
        private BangTraBeTongWindow _bangTraBeTongView;
        private BangTraBeTongVM _bangTraBeTongVM;
     
        public BangTraBeTongAction()
        {
            _bangTraBeTongVM = new BangTraBeTongVM();
            _bangTraBeTongView = new BangTraBeTongWindow() { DataContext = _bangTraBeTongVM };
        }
        public void Execute()
        {
            try
            {
                _bangTraBeTongVM.ImageCDBT = ImageHelper.GetImagePath(StringDefinition.TenBangBT);
                _bangTraBeTongView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BangTraBeTongAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
