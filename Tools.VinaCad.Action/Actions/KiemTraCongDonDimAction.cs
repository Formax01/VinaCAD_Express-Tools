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
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Action.Actions
{
    public class KiemTraCongDonDimAction
    {
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

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if(XLDCDSetting.LechPhuong == 0 ||XLDCDSetting.LechChanDim == 0)
                {
                    UpdateSetting();
                }    
                if (XLDCDSetting.LechPhuong>0 && XLDCDSetting.LechChanDim > 0)
                {
                    string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                    BlockTemplateLoader.LoadDimStylesFromFile(_db, templatePath, new List<string> { StringDefinition.dimCongDonName });

                    _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    _ed.UpdateScreen();

                    List<ObjectId> wrongDimIds = DimHelper.CheckCumulativeDimensions(_db, _ed, 1, XLDCDSetting.LechPhuong, XLDCDSetting.LechChanDim);

                    if (wrongDimIds.Count == 0)
                        return;
                }
                else
                {
                    MessageBox.Show("Bạn chưa xác lập cài đặt dim cộng dồn!", StringDefinition.TITLE_MESSAGE);
                }
                
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(KiemTraCongDonDimAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void UpdateSetting()
        {
            XLDCDSetting.LechPhuong = 10;
            XLDCDSetting.LechChanDim = 0.9;
            XLDCDSetting.IsLeftToRightHorizontal = true;
            XLDCDSetting.IsBottomToTopVertical = true;
            XLDCDSetting.IsBottomToTopAligned = true;
        }
    }
}
