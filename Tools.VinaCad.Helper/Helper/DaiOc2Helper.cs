using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
namespace Tools.VinaCad.Helper.Helper
{
    public static class DaiOc2Helper
    {
        

        private const double HALF_HEIGHT_RATIO = 0.4;     // nửa cao = 0.4 L (200 khi L = 500)
        private const double HALF_TOP_RATIO = 0.8723;  // dùng để suy ra độ vát (giống Bulon3)
        private static readonly double Tan30 = Math.Tan(Math.PI / 6);

        public static ObjectId EnsureBlockDefinition(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId id = EnsureBlock(db, tr);
                tr.Commit();
                return id;
            }
        }

        /// <summary>Ướm block bằng Jig (tâm = trung điểm cạnh đáy, hướng = trục đối xứng), click để đặt.</summary>
        public static bool PlaceWithJig(Document doc, double L, Point3d origin)
        {
            if (doc == null || !double.IsFinite(L) || L <= 0)
                return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId btrId = EnsureBlockDefinition(db);
            if (btrId.IsNull || !btrId.IsValid || btrId.IsErased)
                return false;

            BlockReference br = new BlockReference(origin, btrId);
            br.SetDatabaseDefaults(db);
            br.ScaleFactors = new Scale3d(L);

            // Sau AddNewlyCreatedDBObject, transaction sở hữu wrapper native này.
            bool transactionOwnsReference = false;
            try
            {
                Bulon1Helper.Bulon1Jig jig =
                    new Bulon1Helper.Bulon1Jig(br, origin, ed.CurrentUserCoordinateSystem);

                PromptResult res = ed.Drag(jig);

                if (res.Status != PromptStatus.OK && res.Status != PromptStatus.None)
                    return false;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = tr.GetObject(
                        db.CurrentSpaceId, OpenMode.ForWrite, false) as BlockTableRecord;
                    if (space == null)
                        return false;

                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    transactionOwnsReference = true;
                    tr.Commit();
                }

                return true;
            }
            finally
            {
                if (!transactionOwnsReference)
                    br.Dispose();
            }
        }

        /// <summary>Tạo định nghĩa block $daioc2 (đơn vị L = 1) nếu chưa có.</summary>
        private static ObjectId EnsureBlock(Database db, Transaction tr)
        {
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (bt == null)
                return ObjectId.Null;
            if (bt.Has(StringDefinition.blockNameDaiOc2))
                return bt[StringDefinition.blockNameDaiOc2];

            const double L = 1.0;
            double a = Math.Sqrt(3) / 2 * L;                    // nửa chiều rộng = 0.866 L
            double H = HALF_HEIGHT_RATIO * L;                   // nửa cao = 0.4 L  (200 khi L = 500)
            double yc = H - (L - HALF_TOP_RATIO * L) * Tan30;    // chân cung (163.15 khi L = 500)

            double oy = H;   // dời hình lên H để gốc (0,0) nằm ở trung điểm cạnh đáy

            BlockTableRecord btr = new BlockTableRecord { Name = StringDefinition.blockNameDaiOc2, Origin = Point3d.Origin };

            bt.UpgradeOpen();
            ObjectId btrId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            void Add(Entity e)
            {
                e.SetDatabaseDefaults(db);
                e.LayerId = db.LayerZero;   // layer 0
                e.ColorIndex = 0;           // ByBlock
                e.Linetype = "ByBlock";
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }

            Point3d P(double x, double y) => new Point3d(x, y + oy, 0);

            // 2 cạnh biên chạy suốt từ đường đáy lên đường đỉnh
            Add(new Line(P(-a, -H), P(-a, H)));
            Add(new Line(P(a, -H), P(a, H)));

            // Cạnh giữa chỉ nối 2 chân cung
            Add(new Line(P(0, -yc), P(0, yc)));

            // Mặt trên (sign = +1) và mặt dưới đối xứng (sign = -1)
            foreach (int sign in new[] { 1, -1 })
            {
                // Đường đỉnh / đường đáy suốt chiều rộng
                Add(new Line(P(-a, sign * H), P(a, sign * H)));

                // 2 cung, mỗi mặt rộng a => nửa dây cung = a/2
                AddArc(-a / 2, a / 2, yc, H, sign, oy, Add);
                AddArc(a / 2, a / 2, yc, H, sign, oy, Add);
            }

            return btrId;
        }

        private static void AddArc(double xm, double c, double yc, double H, int sign,
                                   double oy, System.Action<Entity> add)
        {
            double s = H - yc;                          // sagitta
            double R = (c * c + s * s) / (2 * s);
            double theta = Math.Asin(c / R);

            Point3d center = new Point3d(xm, sign * (H - R) + oy, 0);

            // Trên: đỉnh cung ở góc 90°; dưới: đỉnh cung ở góc 270°
            double mid = sign > 0 ? Math.PI / 2 : 3 * Math.PI / 2;
            add(new Arc(center, R, mid - theta, mid + theta));
        }
    }
}
