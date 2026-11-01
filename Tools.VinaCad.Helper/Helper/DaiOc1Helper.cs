using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
namespace Tools.VinaCad.Helper.Helper
{
    public static class DaiOc1Helper
    {
        

        private const double HALF_HEIGHT_RATIO = 0.4;     // nửa cao = 0.4 L (200 khi L = 500)
        private const double HALF_TOP_RATIO = 0.8723;  // nửa đường đỉnh = 0.8723 L
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

        /// <summary>Ướm block bằng jig (tâm = điểm đặt), click để đặt.</summary>
        public static bool PlaceWithJig(Document doc, double L, Point3d center)
        {
            if (doc == null || !double.IsFinite(L) || L <= 0)
                return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId btrId = EnsureBlockDefinition(db);
            if (btrId.IsNull || !btrId.IsValid || btrId.IsErased)
                return false;

            BlockReference br = new BlockReference(center, btrId);
            br.SetDatabaseDefaults(db);
            br.ScaleFactors = new Scale3d(L);

            // Sau AddNewlyCreatedDBObject, transaction sở hữu wrapper native này.
            bool transactionOwnsReference = false;
            try
            {
                // Dùng lại jig của Bulon1
                Bulon1Helper.Bulon1Jig jig =
                    new Bulon1Helper.Bulon1Jig(br, center, ed.CurrentUserCoordinateSystem);

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

        /// <summary>Tạo định nghĩa block $mb-daioc1 (đơn vị L = 1), gốc ở tâm hình.</summary>
        private static ObjectId EnsureBlock(Database db, Transaction tr)
        {
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (bt == null)
                return ObjectId.Null;
            if (bt.Has(StringDefinition.blockNameDaiOc1))
                return bt[StringDefinition.blockNameDaiOc1];

            const double L = 1.0;
            double H = HALF_HEIGHT_RATIO * L;
            double halfTop = HALF_TOP_RATIO * L;
            double yc = H - (L - halfTop) * Tan30;
            double oy = H;

            BlockTableRecord btr = new BlockTableRecord { Name = StringDefinition.blockNameDaiOc1, Origin = Point3d.Origin };

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

            // 4 cạnh đứng: từ chân cung dưới (-yc) đến chân cung trên (+yc)
            foreach (double x in new[] { -L, -L / 2, L / 2, L })
                Add(new Line(P(x, -yc), P(x, yc)));

            // Mặt trên (sign = +1) và mặt dưới đối xứng (sign = -1)
            foreach (int sign in new[] { 1, -1 })
            {
                // Đường đỉnh + 2 đoạn vát 30°
                Add(new Line(P(-halfTop, sign * H), P(halfTop, sign * H)));
                Add(new Line(P(-L, sign * yc), P(-halfTop, sign * H)));
                Add(new Line(P(L, sign * yc), P(halfTop, sign * H)));

                // 3 cung: (tâm x, nửa dây cung)
                AddArc(-0.75 * L, L / 4, yc, H, sign, oy, Add);
                AddArc(0, L / 2, yc, H, sign, oy, Add);
                AddArc(0.75 * L, L / 4, yc, H, sign, oy, Add);
            }

            return btrId;
        }

        private static void AddArc(double xm, double c, double yc, double H, int sign,
                           double oy, System.Action<Entity> add)
        {
            double s = H - yc;
            double R = (c * c + s * s) / (2 * s);
            double theta = Math.Asin(c / R);

            Point3d center = new Point3d(xm, sign * (H - R) + oy, 0);   // + oy

            double mid = sign > 0 ? Math.PI / 2 : 3 * Math.PI / 2;
            add(new Arc(center, R, mid - theta, mid + theta));
        }
    }
}
