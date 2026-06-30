using Prima.VinaCAD.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Helper.Helper
{
    public class ActiveDocumentHelper
    {
        public static Document GetActiveDocument()
        {
            return Application.DocumentManager.MdiActiveDocument;
        }
    }
}
