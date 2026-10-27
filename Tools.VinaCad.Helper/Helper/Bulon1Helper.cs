using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public static class Bulon1Helper
    {
        public const string BLOCK_NAME = "$bulon1";

        private const double HEIGHT_RATIO = 0.7;     // H = 0.7 L
        private const double HALF_TOP_RATIO = 0.8723;  // nửa đường đỉnh = 0.8723 L
        private static readonly double Tan30 = Math.Tan(Math.PI / 6);

        /// <summary>Chèn block $bulon1 với scale = L (tạo định nghĩa nếu chưa có).</summary>
        public static ObjectId Insert(Database db, double L, Point3d origin)
        {
            if (db == null || !double.IsFinite(L) || L <= 0)
                return ObjectId.Null;

            ObjectId refId = ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId btrId = EnsureBlock(db, tr);

                BlockTableRecord space = tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForWrite, false) as BlockTableRecord;
                if (space == null)
                    return ObjectId.Null;

                BlockReference br = new BlockReference(origin, btrId);
                br.SetDatabaseDefaults(db);
                br.ScaleFactors = new Scale3d(L);

                refId = space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                tr.Commit();
            }

            return refId;
        }

        /// <summary>Tạo định nghĩa block $bulon1 (đơn vị L = 1) nếu chưa tồn tại.</summary>
        private static ObjectId EnsureBlock(Database db, Transaction tr)
        {
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (bt == null)
                return ObjectId.Null;
            if (bt.Has(BLOCK_NAME))
                return bt[BLOCK_NAME];

            const double L = 1.0;
            double H = HEIGHT_RATIO * L;
            double halfTop = HALF_TOP_RATIO * L;
            double yc = H - (L - halfTop) * Tan30;

            BlockTableRecord btr = new BlockTableRecord { Name = BLOCK_NAME, Origin = Point3d.Origin };

            bt.UpgradeOpen();
            ObjectId btrId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            void Add(Entity e)
            {
                e.SetDatabaseDefaults(db);
                e.LayerId = db.LayerZero;      // layer 0
                e.ColorIndex = 0;              // ByBlock
                e.Linetype = "ByBlock";
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }

            Point3d P(double x, double y) => new Point3d(x, y, 0);

            // Đáy
            Add(new Line(P(-L, 0), P(L, 0)));

            // 4 cạnh đứng
            foreach (double x in new[] { -L, -L / 2, L / 2, L })
                Add(new Line(P(x, 0), P(x, yc)));

            // Đường đỉnh + 2 đoạn vát 30°
            Add(new Line(P(-halfTop, H), P(halfTop, H)));
            Add(new Line(P(-L, yc), P(-halfTop, H)));
            Add(new Line(P(L, yc), P(halfTop, H)));

            // 3 cung
            AddArc(-0.75 * L, L / 4, yc, H, Add);
            AddArc(0, L / 2, yc, H, Add);
            AddArc(0.75 * L, L / 4, yc, H, Add);

            return btrId;
        }

        private static void AddArc(double xm, double c, double yc, double H, Action<Entity> add)
        {
            double s = H - yc;                          // sagitta
            double R = (c * c + s * s) / (2 * s);
            double theta = Math.Asin(c / R);
            Point3d center = new Point3d(xm, H - R, 0);

            add(new Arc(center, R, Math.PI / 2 - theta, Math.PI / 2 + theta));
        }
        /// <summary>Tạo định nghĩa block nếu chưa có, trả về ObjectId của BlockTableRecord.</summary>
        public static ObjectId EnsureBlockDefinition(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId id = EnsureBlock(db, tr);
                tr.Commit();
                return id;
            }
        }

        /// <summary>Ướm block bằng Jig (tâm = origin, hướng = trục đối xứng), click để đặt.</summary>
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
                Bulon1Jig jig = new Bulon1Jig(br, origin, ed.CurrentUserCoordinateSystem);
                PromptResult res = ed.Drag(jig);

                // OK = click chọn hướng, None = Enter (giữ hướng mặc định)
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
                    br.Dispose();   // hủy: block tạm chưa vào database
            }
        }

        public class Bulon1Jig : EntityJig
        {
            private readonly Point3d _origin;
            private readonly Matrix3d _ucsInverse;
            private readonly double _ucsXAngle;     // góc trục X của UCS so với WCS
            private double _rotation;               // rotation WCS của block
            private Point3d _lastPoint;

            public Bulon1Jig(BlockReference br, Point3d origin, Matrix3d ucs) : base(br)
            {
                _origin = origin;
                _ucsInverse = ucs.Inverse();

                Vector3d xAxis = ucs.CoordinateSystem3d.Xaxis;
                _ucsXAngle = Math.Atan2(xAxis.Y, xAxis.X);

                _rotation = 0;
                _lastPoint = origin;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions opt = new JigPromptPointOptions(
                    Lang.BULON1_HELPER_UI_ChonHuongTrucDoiXung)
                {
                    UseBasePoint = true,
                    BasePoint = _origin,
                    Cursor = CursorType.RubberBand,
                    UserInputControls = UserInputControls.NullResponseAccepted
                };

                PromptPointResult res = prompts.AcquirePoint(opt);

                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.Error)
                    return SamplerStatus.Cancel;

                if (res.Status != PromptStatus.OK)      // Enter
                    return SamplerStatus.NoChange;

                if (res.Value.DistanceTo(_lastPoint) < 1e-9)
                    return SamplerStatus.NoChange;

                _lastPoint = res.Value;

                // Hướng từ tâm đến con trỏ, tính trong UCS
                Vector3d v = (res.Value - _origin).TransformBy(_ucsInverse);
                if (v.Length < 1e-9)
                    return SamplerStatus.NoChange;

                double dirAngle = Math.Atan2(v.Y, v.X) + _ucsXAngle;

                // Trục đối xứng của block là +Y (góc 90°) => rotation = góc hướng - 90°
                _rotation = dirAngle - Math.PI / 2;

                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((BlockReference)Entity).Rotation = _rotation;
                return true;
            }
        }

    }
}
