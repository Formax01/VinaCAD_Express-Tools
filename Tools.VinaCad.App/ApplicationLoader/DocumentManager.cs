//using Prima.VinaCAD.ApplicationServices;
//using PrLogTrackingSystem;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using System.Threading.Tasks;

//namespace Tools.AutoCad.App.ApplicationLoader
//{
//    public class DocumentManager
//    {
//        private static DocumentManager _instance = null;
//        public static DocumentManager Instance
//        {
//            get
//            {
//                if (_instance == null)
//                {
//                    _instance = new DocumentManager();
//                }
//                return _instance;
//            }
//        }
//        public void RegisterCommandEvent(Document doc)
//        {
//            try
//            {
//                //doc.CommandWillStart += Doc_CommandWillStart;
//                //doc.CommandEnded += Doc_CommandEnded;
//                //doc.Database.ObjectModified += Database_ObjectModified;
//                //doc.Database.ObjectReappended += Database_ObjectReappended;
//                //doc.Database.ObjectAppended += Database_ObjectAppended;
//                //doc.Database.ObjectErased += Database_ObjecErased;
//            }
//            catch (Exception ex)
//            {
//                Logger.Info(nameof(RegisterCommandEvent), ex);
//                throw new Exception(ex.Message, ex);
//            }
//        }
//    }
//}
