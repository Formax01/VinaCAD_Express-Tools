using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using WorldDraw = Teigha.GraphicsInterface.WorldDraw;

namespace Tools.VinaCad.Helper.Helper
{
    public enum OC1Kind { Red, Gray }

    /// <summary>Một đoạn thẳng theo tọa độ cục bộ (u: sang phải, v: lên trên).</summary>
    public class OC1Segment
    {
        public double U1, V1, U2, V2;
        public OC1Kind Kind;

        public OC1Segment(double u1, double v1, double u2, double v2, OC1Kind kind)
        {
            U1 = u1; V1 = v1; U2 = u2; V2 = v2; Kind = kind;
        }
    }

    public class OC1Input
    {
        /// <summary>Điểm gốc theo UCS hiện hành.</summary>
        public Point3d Origin { get; set; }
        public double Size { get; set; }
        public double Length { get; set; }
        public double Depth { get; set; }
        /// <summary>Góc quay theo UCS, đơn vị radian.</summary>
        public double Angle { get; set; }
    }

    public static class OC1Helper
    {
        public const string LAYER_RED = "OC1_RED";
        public const string LAYER_GRAY = "OC1_GRAY";

        public static short GetColorIndex(OC1Kind kind) =>
            kind == OC1Kind.Red ? (short)1 : (short)9;

        public static List<OC1Segment> BuildSegments(double size, double length, double depth)
        {
            if (!double.IsFinite(size) || !double.IsFinite(length) || !double.IsFinite(depth) ||
                size <= 0 || length <= 0 || depth <= 0 || depth > length)
                return new List<OC1Segment>();

            double offset = size / 10;
            List<OC1Segment> segments = new List<OC1Segment>
            {
                new OC1Segment(0, 0, 0, -length, OC1Kind.Red),
                new OC1Segment(size, 0, size, -length, OC1Kind.Red),
                new OC1Segment(0, -depth, size, -depth, OC1Kind.Gray),
                new OC1Segment(offset, 0, offset, -depth, OC1Kind.Gray),
                new OC1Segment(size - offset, 0, size - offset, -depth, OC1Kind.Gray)
            };

            // Điểm chọn là trung điểm hai đầu dưới của hai đường đỏ.
            double du = -size / 2;
            double dv = length;
            foreach (OC1Segment segment in segments)
            {
                segment.U1 += du; segment.U2 += du;
                segment.V1 += dv; segment.V2 += dv;
            }
            return segments;
        }

        /// <summary>Đổi tọa độ cục bộ đã xoay sang UCS.</summary>
        public static Point3d MapToUcs(Point3d originUcs, double angleUcs, double u, double v)
        {
            double cos = Math.Cos(angleUcs);
            double sin = Math.Sin(angleUcs);
            return new Point3d(
                originUcs.X + u * cos - v * sin,
                originUcs.Y + u * sin + v * cos,
                originUcs.Z);
        }

        /// <summary>
        /// Đổi từ hệ cục bộ của OC sang WCS. Preview và entity thật đều dùng hàm
        /// này, nên sẽ không lệch nhau khi UCS đang xoay.
        /// </summary>
        public static Point3d MapToWorld(
            Point3d originUcs, Matrix3d ucs, double angleUcs, double u, double v)
        {
            return MapToUcs(originUcs, angleUcs, u, v).TransformBy(ucs);
        }

        public class OC1Jig : DrawJig
        {
            private readonly Point3d _originUcs;
            private readonly Matrix3d _ucs;
            private readonly IReadOnlyList<OC1Segment> _segments;
            private Point3d _cursorUcs;

            public double Angle { get; private set; }

            public OC1Jig(Point3d originUcs, Matrix3d ucs, IReadOnlyList<OC1Segment> segments)
            {
                _originUcs = originUcs;
                _ucs = ucs;
                _segments = segments ?? Array.Empty<OC1Segment>();
                _cursorUcs = originUcs;
                Angle = 0;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions options = new JigPromptPointOptions(Lang.OC1_HELPER_UI_ChonGocQuay)
                {
                    UseBasePoint = true,
                    BasePoint = _originUcs,
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };

                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.Error)
                    return SamplerStatus.Cancel;
                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.NoChange;

                Point3d cursorUcs = result.Value;
                if (cursorUcs.DistanceTo(_cursorUcs) < 1e-9)
                    return SamplerStatus.NoChange;

                double dx = cursorUcs.X - _originUcs.X;
                double dy = cursorUcs.Y - _originUcs.Y;
                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
                    return SamplerStatus.NoChange;

                _cursorUcs = cursorUcs;
                // Trục đối xứng của hình là local +v, nên local +v hướng về con trỏ.
                Angle = Math.Atan2(dy, dx) - Math.PI / 2;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                foreach (OC1Segment segment in _segments)
                {
                    if (!IsValid(segment))
                        continue;

                    Point3d start = MapToWorld(_originUcs, _ucs, Angle, segment.U1, segment.V1);
                    Point3d end = MapToWorld(_originUcs, _ucs, Angle, segment.U2, segment.V2);
                    draw.SubEntityTraits.Color = GetColorIndex(segment.Kind);
                    draw.Geometry.WorldLine(start, end);
                }
                return true;
            }
        }

        /// <returns>Số Line đã tạo.</returns>
        public static int Create(Database db, Matrix3d ucs, OC1Input input)
        {
            if (input == null || !IsFinite(input.Origin) || !double.IsFinite(input.Size) ||
                !double.IsFinite(input.Length) || !double.IsFinite(input.Depth) ||
                !double.IsFinite(input.Angle) || input.Size <= 0 || input.Length <= 0 ||
                input.Depth <= 0 || input.Depth > input.Length)
                return 0;

            return CreateLines(db, ucs, input.Origin, input.Angle,
                BuildSegments(input.Size, input.Length, input.Depth));
        }

        /// <summary>Tạo Line thật từ danh sách đoạn cục bộ, dùng cùng phép đổi tọa độ với jig.</summary>
        public static int CreateLines(
            Database db, Matrix3d ucs, Point3d originUcs, double angleUcs,
            IReadOnlyList<OC1Segment> segments)
        {
            if (db == null || segments == null || segments.Count == 0 ||
                !IsFinite(originUcs) || !double.IsFinite(angleUcs))
                return 0;

            int count = 0;
            using (Transaction transaction = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = transaction.GetObject(
                    db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
                if (blockTable == null || !blockTable.Has(BlockTableRecord.ModelSpace))
                    return 0;

                BlockTableRecord modelSpace = transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite, false) as BlockTableRecord;
                if (modelSpace == null)
                    return 0;

                ObjectId redLayer = EnsureLayer(transaction, db, LAYER_RED, 1);
                ObjectId grayLayer = EnsureLayer(transaction, db, LAYER_GRAY, 9);
                if (redLayer.IsNull || grayLayer.IsNull)
                    return 0;

                foreach (OC1Segment segment in segments)
                {
                    if (!IsValid(segment))
                        return 0;

                    Point3d start = MapToWorld(originUcs, ucs, angleUcs, segment.U1, segment.V1);
                    Point3d end = MapToWorld(originUcs, ucs, angleUcs, segment.U2, segment.V2);
                    Line line = new Line(start, end);
                    line.SetDatabaseDefaults(db);
                    line.LayerId = segment.Kind == OC1Kind.Red ? redLayer : grayLayer;
                    line.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    modelSpace.AppendEntity(line);
                    transaction.AddNewlyCreatedDBObject(line, true);
                    count++;
                }

                transaction.Commit();
            }
            return count;
        }

        private static ObjectId EnsureLayer(
            Transaction transaction, Database db, string name, short colorIndex)
        {
            LayerTable layerTable = transaction.GetObject(
                db.LayerTableId, OpenMode.ForRead, false) as LayerTable;
            if (layerTable == null)
                return ObjectId.Null;
            if (layerTable.Has(name))
                return layerTable[name];

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            ObjectId id = layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        private static bool IsValid(OC1Segment segment) =>
            segment != null && double.IsFinite(segment.U1) && double.IsFinite(segment.V1) &&
            double.IsFinite(segment.U2) && double.IsFinite(segment.V2);

        private static bool IsFinite(Point3d point) =>
            double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }
}
