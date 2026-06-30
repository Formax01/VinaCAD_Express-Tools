using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Resources.Definitions;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class ImportAllFromTemplatesAction
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
                string templatePath = BlockTemplateLoader.GetTemplatePath("TemplateBlocks.dwg");

                bool ok = BlockTemplateLoader.LoadAllObjectsFromFile(_db,_ed, templatePath, DuplicateRecordCloning.Ignore);
                if(ok)
                {
                    MessageBox.Show("Đã load toàn bộ đối tượng từ file template!", StringDefinition.TITLE_MESSAGE);
                }    
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ImportAllFromTemplatesAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

    }
}
