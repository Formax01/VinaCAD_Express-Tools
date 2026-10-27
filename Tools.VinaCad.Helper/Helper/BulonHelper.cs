using System;
using Prima.VinaCAD.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;

namespace Tools.VinaCad.Helper.Helper
{
    public class BulonHelper
    {
        public const string BLOCK_NAME = "$mb-bulon";
        public const string DAIOC_BLOCK_NAME = "$mb-daioc";

        private const string CENTER_LT = "CENTER";
        private const double AXIS_EXTEND = 1.25;      // đường tâm dài 2.5 (đơn vị L = 1)
        private const short AXIS_COLOR_INDEX = 8;     // Color 8

        
        public static int CreateBolt(Database db, Editor ed, Point3d centerUcs, double l)
        {
            return CreateBolt(db, ed, centerUcs, l, BLOCK_NAME, false);
        }

        
        public static int CreateBolt(
            Database db, Editor ed, Point3d centerUcs, double l,
            string blockName, bool withInnerCircle)
        {
            if (db == null || ed == null || string.IsNullOrWhiteSpace(blockName) ||
                !double.IsFinite(l) || l <= 0)
                return 0;

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;

            // LoadLineTypeFile thay đổi database theo cách riêng của Teigha. Không gọi
            // nó trong một transaction đang mở để tránh trạng thái lock lồng nhau.
            EnsureCenterLinetype(db);

            // 1. Đảm bảo block tồn tại
            ObjectId blockId;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                blockId = EnsureBoltBlock(db, tr, blockName, withInnerCircle);
                tr.Commit();
            }

            if (blockId.IsNull || !blockId.IsValid || blockId.IsErased)
                return 0;

            // 2. Block ref tạm (chưa thêm vào database) để Jig ướm hình
            BlockReference br = new BlockReference(Point3d.Origin, blockId);
            // Sau AddNewlyCreatedDBObject, transaction sở hữu wrapper native này,
            // kể cả khi Commit thất bại và transaction phải rollback.
            bool transactionOwnsReference = false;
            try
            {
                br.SetDatabaseDefaults(db);
                br.LayerId = db.LayerZero;                       // Layer 0

                BulonJig jig = new BulonJig(br, ucs, centerUcs, l);
                PromptResult dragResult = ed.Drag(jig);

                if (dragResult.Status != PromptStatus.OK)
                    return 0;

                // 3. Ghi block ref vào bản vẽ
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = tr.GetObject(
                        db.CurrentSpaceId, OpenMode.ForWrite, false) as BlockTableRecord;
                    if (space == null)
                        return 0;

                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    transactionOwnsReference = true;

                    tr.Commit();
                }

                return 1;
            }
            finally
            {
                if (!transactionOwnsReference)
                    br.Dispose();
            }
        }

        /// <summary>
        /// Jig xoay bulon quanh tâm; trục tâm -> đỉnh lục giác trùng hướng tâm -> con trỏ.
        /// </summary>
        private class BulonJig : EntityJig
        {
            private readonly Matrix3d _ucs;
            private readonly Point3d _center;     // theo UCS
            private readonly double _scale;
            private double _angle;                // theo UCS
            private Point3d _lastPoint;

            public BulonJig(BlockReference br, Matrix3d ucs, Point3d centerUcs, double l)
                : base(br)
            {
                _ucs = ucs;
                _center = centerUcs;
                _scale = l;
                _angle = 0;
                _lastPoint = centerUcs;
                Update();
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions opt = new JigPromptPointOptions(
                    Lang.BULON_HELPER_UI_ChonHuongBulon)
                {
                    UseBasePoint = true,
                    BasePoint = _center,
                    Cursor = CursorType.RubberBand,
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };

                PromptPointResult res = prompts.AcquirePoint(opt);

                if (res.Status == PromptStatus.Cancel)
                    return SamplerStatus.Cancel;

                if (res.Status != PromptStatus.OK)
                    return SamplerStatus.NoChange;

                Vector3d v = res.Value - _center;

                if (v.Length < 1e-9 || res.Value.DistanceTo(_lastPoint) < 1e-9)
                    return SamplerStatus.NoChange;

                _lastPoint = res.Value;
                _angle = Math.Atan2(v.Y, v.X);

                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                BlockReference br = (BlockReference)Entity;

                // scale (L) -> xoay quanh tâm -> dời về tâm -> UCS (-> WCS)
                br.BlockTransform =
                    _ucs
                    * Matrix3d.Displacement(_center.GetAsVector())
                    * Matrix3d.Rotation(_angle, Vector3d.ZAxis, Point3d.Origin)
                    * Matrix3d.Scaling(_scale, Point3d.Origin);

                return true;
            }
        }

        /// <summary>Tạo block $mb-bulon (L = 1) nếu chưa có, trả về ObjectId.</summary>
        private static ObjectId EnsureBoltBlock(
    Database db, Transaction tr, string blockName, bool withInnerCircle)
        {
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (bt == null)
                return ObjectId.Null;

            if (bt.Has(blockName))
                return bt[blockName];

            bt.UpgradeOpen();

            BlockTableRecord btr = new BlockTableRecord
            {
                Name = blockName,
                Origin = Point3d.Origin
            };

            ObjectId blockId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            // 1. Lục giác: tâm -> đỉnh = 1 (một đỉnh nằm trên trục X)
            Polyline hex = new Polyline();
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 3.0 * i;
                hex.AddVertexAt(i, new Point2d(Math.Cos(a), Math.Sin(a)), 0, 0, 0);
            }
            hex.Closed = true;
            AddToBlock(btr, tr, hex, true, db);

            // 2. Hình tròn nội tiếp: r = sqrt(3) / 2
            Circle circle = new Circle(Point3d.Origin, Vector3d.ZAxis, Math.Sqrt(3.0) / 2.0);
            AddToBlock(btr, tr, circle, true, db);

            // 2b. DAIOC: thêm vòng tròn r = L / 2 (L = 1 trong block -> 0.5), tâm = tâm block
            if (withInnerCircle)
            {
                Circle inner = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.5);
                AddToBlock(btr, tr, inner, true, db);
            }

            // 3. Hai đường tâm: Polyline CENTER, Color 8, scale 1, Plinegen Disabled
            ObjectId ltId = GetLinetypeId(db, tr, CENTER_LT);
            AddCenterPolyline(btr, tr, db, ltId, true);     // ngang
            AddCenterPolyline(btr, tr, db, ltId, false);    // dọc

            return blockId;
        }

        private static void AddCenterPolyline(
            BlockTableRecord btr, Transaction tr, Database db, ObjectId ltId, bool horizontal)
        {
            Point2d p1 = horizontal ? new Point2d(-AXIS_EXTEND, 0) : new Point2d(0, -AXIS_EXTEND);
            Point2d p2 = horizontal ? new Point2d(AXIS_EXTEND, 0) : new Point2d(0, AXIS_EXTEND);

            Polyline axis = new Polyline();
            axis.AddVertexAt(0, p1, 0, 0, 0);
            axis.AddVertexAt(1, p2, 0, 0, 0);
            axis.Closed = false;
            axis.Elevation = 0;
            axis.ConstantWidth = 0;                          // Global width = 0

            AddToBlock(btr, tr, axis, false, db);

            axis.Color = Color.FromColorIndex(ColorMethod.ByAci, AXIS_COLOR_INDEX);   // Color 8
            if (!ltId.IsNull)
                axis.LinetypeId = ltId;                      // CENTER
            axis.LinetypeScale = 1;                          // Linetype scale = 1
            axis.LineWeight = LineWeight.ByLayer;            // Lineweight ByLayer
            axis.Plinegen = false;               // Linetype generation: Disabled
        }

        private static void AddToBlock(
            BlockTableRecord btr, Transaction tr, Entity ent, bool byBlock, Database db)
        {
            ent.SetDatabaseDefaults(db);
            ent.LayerId = db.LayerZero;                      // layer 0

            if (byBlock)
                ent.Color = Color.FromColorIndex(ColorMethod.ByBlock, 0);

            btr.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        private static void EnsureCenterLinetype(Database db)
        {
            bool exists;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LinetypeTable lt = tr.GetObject(
                    db.LinetypeTableId, OpenMode.ForRead, false) as LinetypeTable;
                exists = lt != null && lt.Has(CENTER_LT);
            }

            if (exists)
                return;

            try
            {
                
                db.LoadLineTypeFile(CENTER_LT, "acad.lin");
            }
            catch { /* đường tâm sẽ dùng nét liền */ }
        }

        private static ObjectId GetLinetypeId(Database db, Transaction tr, string name)
        {
            LinetypeTable lt = tr.GetObject(db.LinetypeTableId, OpenMode.ForRead) as LinetypeTable;
            return (lt != null && lt.Has(name)) ? lt[name] : ObjectId.Null;
        }

    }
}
