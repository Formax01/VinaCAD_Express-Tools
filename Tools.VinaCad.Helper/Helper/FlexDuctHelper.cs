using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public enum FlexDuctEnd
    {
        Flat,   
        Box,    
        Dome    
    }

    public sealed class FlexDuctStyle
    {
        public double BendRadiusFactor = 1.5;   // bán kính bo đường tâm TỐI THIỂU = x * D
        public double PitchFactor = 0.3;        // bước gân = x * D
        public double RibBulge = 0.18;          // cung gân (phồng theo chiều đi) = độ phồng / (D/2)
        public double CapBulge = 0.5;           // cung nối 2 gân kề nhau ở mép (phồng ra ngoài)
        public double CapBulgeBare = -0.5;      // cung nối 2 gân kề nhau ở mép (lún vào trong)
        public bool IsBare = false;
        public double InnerArcBulge = 0.44;     // cung phụ trong vòm đầu ống
        public double DomeBulge = 1.0;          // 1.0 = bán nguyệt
        public double BoxDepthFactor = 0.55;    // độ dày hộp = x * (D/2)
        public FlexDuctEnd StartEnd = FlexDuctEnd.Box;
        public FlexDuctEnd EndEnd = FlexDuctEnd.Dome;
        public double MaxBendRadiusFactor = 10.0;// bán kính bo TỐI ĐA = x * D (chân càng dài thì bo góc cầng rộng )
        public double BendLegRatio = 0.35;       // đoạn bo ≈ 35% chiều dài đoạn thẳng ngắn hơn kề góc
    }

    public static class FlexDuctStyles
    {
        public static FlexDuctStyle For(string name)
        {
            switch ((name ?? string.Empty).ToUpperInvariant())
            {
                case "S2": return S2();
                case "S3": return S3();
                case "R1": return R1();
                case "R2": return R2();
                case "R3": return R3();
                default: return S1();
            }
        }

        public static FlexDuctStyle S1() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Box, EndEnd = FlexDuctEnd.Dome };
        public static FlexDuctStyle S2() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Box, EndEnd = FlexDuctEnd.Box };
        public static FlexDuctStyle S3() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Dome, EndEnd = FlexDuctEnd.Dome };


        public static FlexDuctStyle R1() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Box, EndEnd = FlexDuctEnd.Dome, IsBare= true };
        public static FlexDuctStyle R2() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Box, EndEnd = FlexDuctEnd.Box, IsBare= true };
        public static FlexDuctStyle R3() => new FlexDuctStyle { StartEnd = FlexDuctEnd.Dome, EndEnd = FlexDuctEnd.Dome, IsBare= true };
    }

    public static class FlexDuctHelper
    {
        private const double Epsilon = 1e-9;

        #region Public API

        public static (int RingCount, string GroupName, string? Warning) Draw(Database database, IList<Point3d> path, FlexDuctModel settings, FlexDuctStyle style)
        {
            if (path == null || path.Count < 2 || settings.Diameter <= 0)
                return (0, string.Empty, null);

            double elevation = path[0].Z;
            List<Point2d> points = CleanPoints(path);
            if (points.Count < 2) return (0, string.Empty, null);

            double diameter = settings.Diameter;
            double h = diameter / 2.0;
            double pitch = Math.Max(diameter * style.PitchFactor, 1.0);

            var warnings = new List<string>();

            List<DuctSeg> segs = BuildCenterline(points,
                diameter * style.BendRadiusFactor,
                diameter * style.MaxBendRadiusFactor,
                style.BendLegRatio,
                h, warnings);
            if (segs.Count == 0) return (0, string.Empty, null);

            double totalLength = 0;
            foreach (DuctSeg s in segs) totalLength += s.Length;

            FlexDuctEnd startEnd = style.StartEnd;
            FlexDuctEnd endEnd = style.EndEnd;
            double startDepth = EndDepth(startEnd, h, style);
            double endDepth = EndDepth(endEnd, h, style);

            double body = totalLength - startDepth - endDepth;
            int n = (int)Math.Round(body / pitch);
            if (n < 1)
            {
                warnings.Add("Đường tâm quá ngắn so với hai đầu ống, bỏ phần đầu ống.");
                startEnd = endEnd = FlexDuctEnd.Flat;
                startDepth = endDepth = 0;
                body = totalLength;
                n = (int)Math.Round(body / pitch);
                if (n < 1) return (0, string.Empty, "Đường tâm quá ngắn để vẽ ống.");
            }
            pitch = body / n;  

            var a = new Point2d[n + 1];
            var b = new Point2d[n + 1];
            var us = new Vector2d[n + 1];
            for (int k = 0; k <= n; k++)
            {
                Sample(segs, startDepth + k * pitch, out Point2d p, out Vector2d u);
                Vector2d nrm = new Vector2d(-u.Y, u.X);
                a[k] = p + nrm * h;
                b[k] = p - nrm * h;
                us[k] = u;
            }

            var ids = new ObjectIdCollection();

            using Transaction transaction = database.TransactionManager.StartTransaction();
            BlockTable table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            void Add(Entity entity)
            {
                entity.ColorIndex = 7;
                modelSpace.AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
                ids.Add(entity.ObjectId);
            }

            double cap = style.IsBare ? style.CapBulgeBare : style.CapBulge;  
            for (int k = 0; k < n; k++)
            {
                var ring = new Polyline { Elevation = elevation };
                ring.AddVertexAt(0, a[k], -cap, 0, 0);                  
                ring.AddVertexAt(1, a[k + 1], -style.RibBulge, 0, 0);
                ring.AddVertexAt(2, b[k + 1], -cap, 0, 0);            
                ring.AddVertexAt(3, b[k], +style.RibBulge, 0, 0);
                ring.Closed = true;
                Add(ring);
            }

            switch (startEnd)
            {
                case FlexDuctEnd.Box:
                    {
                        Sample(segs, 0.0, out Point2d p0, out Vector2d u0);
                        Vector2d n0 = new Vector2d(-u0.Y, u0.X);
                        Add(CreateBox(p0 + n0 * h, a[0], b[0], p0 - n0 * h));
                        break;
                    }
                case FlexDuctEnd.Dome:
                    Add(TwoPoint(a[0], b[0], +style.DomeBulge, elevation));      
                    Add(TwoPoint(a[0], b[0], +style.InnerArcBulge, elevation));
                    break;
            }

            switch (endEnd)
            {
                case FlexDuctEnd.Box:
                    {
                        Sample(segs, startDepth + n * pitch + endDepth, out Point2d pl, out Vector2d ul);
                        Vector2d nl = new Vector2d(-ul.Y, ul.X);
                        Add(CreateBox(a[n], pl + nl * h, pl - nl * h, b[n]));
                        break;
                    }
                case FlexDuctEnd.Dome:
                    Add(TwoPoint(a[n], b[n], -style.DomeBulge, elevation));      // bán nguyệt phồng ra phía trước
                    Add(TwoPoint(a[n], b[n], -style.InnerArcBulge, elevation));  // cung phụ
                    break;
            }

            DBDictionary groups = (DBDictionary)transaction.GetObject(database.GroupDictionaryId, OpenMode.ForWrite);
            string groupName = GetUniqueGroupName(groups);
            var group = new Group("Flex duct", true) { Selectable = true };
            groups.SetAt(groupName, group);
            transaction.AddNewlyCreatedDBObject(group, true);
            group.Append(ids);

            transaction.Commit();
            return (ids.Count, groupName, warnings.Count > 0 ? string.Join("; ", warnings) : null);

            Polyline CreateBox(Point2d p1, Point2d p2, Point2d p3, Point2d p4)
            {
                var box = new Polyline { Elevation = elevation };
                box.AddVertexAt(0, p1, 0, 0, 0);
                box.AddVertexAt(1, p2, 0, 0, 0);
                box.AddVertexAt(2, p3, 0, 0, 0);
                box.AddVertexAt(3, p4, 0, 0, 0);
                box.Closed = true;
                return box;
            }
        }

        public static bool TryGetPath(Entity entity, out List<Point3d> path)
        {
            path = new List<Point3d>();
            switch (entity)
            {
                case Line line:
                    path.Add(line.StartPoint);
                    path.Add(line.EndPoint);
                    break;
                case Polyline polyline:
                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                        path.Add(polyline.GetPoint3dAt(i));
                    if (polyline.Closed && path.Count > 2) path.Add(path[0]);
                    break;
                default:
                    return false;
            }
            return path.Count >= 2;
        }

        #endregion

        #region Hình học

        private static double EndDepth(FlexDuctEnd end, double h, FlexDuctStyle style)
        {
            switch (end)
            {
                case FlexDuctEnd.Box: return h * style.BoxDepthFactor;
                case FlexDuctEnd.Dome: return h * style.DomeBulge;   // bulge * h = độ sâu vòm
                default: return 0.0;
            }
        }

        private static Polyline TwoPoint(Point2d from, Point2d to, double bulge, double elevation)
        {
            var pl = new Polyline { Elevation = elevation };
            pl.AddVertexAt(0, from, bulge, 0, 0);
            pl.AddVertexAt(1, to, 0, 0, 0);
            return pl;
        }

        #endregion

        #region Đường tâm (đoạn thẳng + cung bo góc)

        private abstract class DuctSeg
        {
            public double Length;
            public abstract Point2d PointAt(double d);
            public abstract Vector2d TangentAt(double d);
        }

        private sealed class LineSeg : DuctSeg
        {
            private readonly Point2d _a;
            private readonly Vector2d _u;

            public LineSeg(Point2d a, Point2d b)
            {
                _a = a;
                Vector2d v = b - a;
                Length = v.Length;
                _u = Length > Epsilon ? v.GetNormal() : new Vector2d(1, 0);
            }

            public override Point2d PointAt(double d) => _a + _u * d;
            public override Vector2d TangentAt(double d) => _u;
        }

        private sealed class ArcSeg : DuctSeg
        {
            private readonly Point2d _center;
            private readonly Point2d _start;
            private readonly Vector2d _startTangent;
            private readonly double _radius;
            private readonly int _sign;   

            public ArcSeg(Point2d center, double radius, Point2d start, Vector2d startTangent, int sign, double sweep)
            {
                _center = center; _radius = radius; _start = start;
                _startTangent = startTangent; _sign = sign;
                Length = radius * sweep;
            }

            public override Point2d PointAt(double d)
                => _center + (_start - _center).RotateBy(_sign * d / _radius);

            public override Vector2d TangentAt(double d)
                => _startTangent.RotateBy(_sign * d / _radius);
        }

        private static List<DuctSeg> BuildCenterline(
            List<Point2d> pts, double radius, double maxRadius, double legRatio,
            double halfWidth, List<string> warnings)
        {
            var segs = new List<DuctSeg>();
            Point2d cursor = pts[0];
            int last = pts.Count - 1;

            for (int i = 1; i < last; i++)
            {
                Point2d p0 = pts[i - 1], p1 = pts[i], p2 = pts[i + 1];
                Vector2d vin = p1 - p0, vout = p2 - p1;
                double lin = vin.Length, lout = vout.Length;
                if (lin < Epsilon || lout < Epsilon) continue;

                Vector2d uin = vin.GetNormal(), uout = vout.GetNormal();
                double cos = Math.Max(-1.0, Math.Min(1.0, uin.DotProduct(uout)));
                double theta = Math.Acos(cos);
                if (theta < 1e-6 || Math.PI - theta < 1e-6) continue; // thẳng hàng hoặc quay đầu 180°

                double tanHalf = Math.Tan(theta / 2.0);
                double availIn = (i - 1 == 0) ? lin : lin / 2.0;
                double availOut = (i + 1 == last) ? lout : lout / 2.0;

                //bo thích nghi - chân càng dài bán kính càng lớn
                double minT = radius * tanHalf;
                double maxT = Math.Max(minT, maxRadius * tanHalf);
                double wantT = Math.Max(minT, Math.Min(maxT, Math.Min(lin, lout) * legRatio));
                double t = Math.Min(wantT, Math.Min(availIn, availOut));
                double r = t / tanHalf;

                if (r < radius - 1e-6)
                    warnings.Add($"Góc {i}: đoạn ngắn, bán kính bo giảm còn {r:0.##}");
                if (r <= halfWidth)
                    warnings.Add($"Góc {i}: bán kính {r:0.##} <= D/2, mép trong bị gấp");

                Point2d t1 = p1 - uin * t;
                Point2d t2 = p1 + uout * t;

                double cross = uin.X * uout.Y - uin.Y * uout.X;
                int sign = cross > 0 ? 1 : -1;
                Vector2d toCenter = new Vector2d(-uin.Y, uin.X) * sign;
                Point2d center = t1 + toCenter * r;

                if (t1.GetDistanceTo(cursor) > Epsilon) segs.Add(new LineSeg(cursor, t1));
                segs.Add(new ArcSeg(center, r, t1, uin, sign, theta));
                cursor = t2;
            }

            if (pts[last].GetDistanceTo(cursor) > Epsilon)
                segs.Add(new LineSeg(cursor, pts[last]));

            return segs;
        }

        private static void Sample(List<DuctSeg> segs, double distance, out Point2d point, out Vector2d tangent)
        {
            double rest = Math.Max(0.0, distance);
            foreach (DuctSeg s in segs)
            {
                if (rest <= s.Length + Epsilon)
                {
                    rest = Math.Min(rest, s.Length);
                    point = s.PointAt(rest);
                    tangent = s.TangentAt(rest);
                    return;
                }
                rest -= s.Length;
            }
            DuctSeg e = segs[segs.Count - 1];
            point = e.PointAt(e.Length);
            tangent = e.TangentAt(e.Length);
        }

        #endregion

        #region Tiện ích

        private static List<Point2d> CleanPoints(IList<Point3d> path)
        {
            var result = new List<Point2d>();
            foreach (Point3d p in path)
            {
                var q = new Point2d(p.X, p.Y);
                if (result.Count == 0 || q.GetDistanceTo(result[result.Count - 1]) > Epsilon)
                    result.Add(q);
            }
            return result;
        }

        private static string GetUniqueGroupName(DBDictionary groups)
        {
            for (int index = 1; ; index++)
            {
                string name = $"FLEXDUCT_{index}";
                if (!groups.Contains(name)) return name;
            }
        }

        #endregion
    }

    //class tạo đường tâm tạm màu xanh lá khi đang chọn điểm 
    public sealed class FlexDuctPathPreview : IDisposable
    {
        private readonly Database _db;
        private ObjectId _id = ObjectId.Null;

        public FlexDuctPathPreview(Database db) { _db = db; }

        public void Update(IList<Point3d> points)
        {
            Clear();
            if (points == null || points.Count < 2) return;

            using Transaction tr = _db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var pl = new Polyline { Elevation = points[0].Z, ColorIndex = 3 }; 
            for (int i = 0; i < points.Count; i++)
                pl.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            _id = pl.ObjectId;
            tr.Commit();
        }

        public void Clear()
        {
            if (_id.IsNull || _id.IsErased) { _id = ObjectId.Null; return; }
            using Transaction tr = _db.TransactionManager.StartTransaction();
            var ent = (Entity)tr.GetObject(_id, OpenMode.ForWrite);
            ent.Erase();
            tr.Commit();
            _id = ObjectId.Null;
        }

        public void Dispose() => Clear();
    }
}