using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Tools.View.UI;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCad.Action.Actions
{
    public class KiemTraTrungDimAction
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
                _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                _ed.UpdateScreen();
                List<ObjectId> duplicateDimIds = DimHelper.CheckDuplicateDimensions(_db, _ed);

                if (duplicateDimIds == null || duplicateDimIds.Count == 0)
                    return;
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(KiemTraTrungDimAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }


}
