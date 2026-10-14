using System;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class DuctElbowHelper
    {
        private const double Eps = 1e-6;

        public static List<ObjectId> Draw(Database db, Transaction tr, DuctElbowModel m)
        {
            List<Entity> ents = m.Type == DuctElbowType.Vertical
                ? BuildVerticalElbow(db, tr, m)
                : BuildElbow(m);

            EnsureLayer(db, tr, m.LayerName);

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            var ids = new List<ObjectId>();
            foreach (Entity e in ents)
            {
                if (!string.IsNullOrEmpty(m.LayerName) && e.Layer == "0") e.Layer = m.LayerName;
                ids.Add(ms.AppendEntity(e));
                tr.AddNewlyCreatedDBObject(e, true);
            }

            CreateGroup(db, tr, ids);
            return ids;
        }

        public static List<Entity> BuildElbow(DuctElbowModel m)
        {
            var result = new List<Entity>();

            double s = m.Side == ElbowSide.Left ? 1.0 : -1.0;   // +1: rẽ trái, -1: rẽ phải
            double theta = s * m.AngleDeg * Math.PI / 180.0;
            double z = m.InsertPoint.Z;

            Point2d P = new Point2d(m.InsertPoint.X, m.InsertPoint.Y);
            Vector2d d = new Vector2d(m.Direction.X, m.Direction.Y).GetNormal();

            // nIn: pháp tuyến hướng về TÂM cung rẽ
            Vector2d nIn = d.RotateBy(s * Math.PI / 2.0);
            Point2d O = P + nIn * m.CenterRadius;                 // tâm cung đường tim

            // Đường tim: điểm cuối + hướng cuối
            Point2d Pe = O + (P - O).RotateBy(theta);
            Vector2d de = d.RotateBy(theta);
            Vector2d nOut = nIn.RotateBy(theta);

            double h1 = m.Width1 / 2.0;
            double h2 = m.Width2 / 2.0;

            // R là bán kính từ tâm cung đến cung trong.
            double innerRadius = m.CenterRadius - h1;

            // Điểm đầu vào
            Point2d inS = O - nIn * innerRadius;
            Point2d outS = O - nIn * (innerRadius + m.Width1);

            // Điểm đầu ra
            Point2d inE = O - nOut * innerRadius;
            Point2d outE = O - nOut * (innerRadius + m.Width2);

            // Hai thành cong (arc tiếp tuyến với ống thẳng ở đầu vào)
            AddArcWall(result, inS, inE, d, z);
            AddArcWall(result, outS, outE, d, z);

            // Hai miệng ống (đầu vào / đầu ra)
            result.Add(new Line(To3(inS, z), To3(outS, z)));
            result.Add(new Line(To3(inE, z), To3(outE, z)));

            // Đường tim (tùy chọn)
            if (m.ShowCenterLine)
            {
                Polyline c = MakeArcPolyline(P, Pe, d, z);
                if (c != null)
                {
                    c.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);   
                    result.Add(c);
                }
            }

            // Nhãn kích thước
            if (m.ShowLabel)
            {
                string txt = m.Type == DuctElbowType.Reducing
                    ? string.Format("{0:0.##}x{1:0.##}", m.Width1, m.Width2)
                    : string.Format("{0:0.##}", m.Width1);
                result.Add(MakeText(To3(O + (P - O).RotateBy(theta / 2.0), z), txt,
                    Math.Max(m.Width1, m.Width2) * 0.25));
            }

            return result;
        }

        private static void AddArcWall(List<Entity> list, Point2d s, Point2d e, Vector2d startDir, double z)
        {
            Polyline pl = MakeArcPolyline(s, e, startDir, z);
            if (pl != null) list.Add(pl);
        }

        private static Polyline MakeArcPolyline(Point2d s, Point2d e, Vector2d startDir, double z)
        {
            Vector2d chord = e - s;
            if (chord.Length < Eps) return null;

            double phi = Math.Atan2(
                startDir.X * chord.Y - startDir.Y * chord.X,    
                startDir.X * chord.X + startDir.Y * chord.Y);  
            double bulge = Math.Tan(phi / 2.0);

            var pl = new Polyline();
            pl.Elevation = z;
            pl.AddVertexAt(0, s, bulge, 0, 0);
            pl.AddVertexAt(1, e, 0, 0, 0);
            return pl;
        }

        public static List<Entity> BuildVerticalElbow(Database db, Transaction tr, DuctElbowModel m)
        {
            var result = new List<Entity>();

            double z = m.InsertPoint.Z;
            Point2d P = new Point2d(m.InsertPoint.X, m.InsertPoint.Y);
            Vector2d d = new Vector2d(m.Direction.X, m.Direction.Y).GetNormal();
            Vector2d n = d.RotateBy(Math.PI / 2.0);

            double W = m.Width1;
            double h = W / 2.0;
            double Lf = m.CenterRadius + h;
            double Ls = Math.Max(Lf - W, 0.0);

            Func<double, double, Point2d> pt = (a, b) => P + d * a + n * b;

            string downLinetype = null;

            if (m.VerticalDir == VerticalDirection.Down && db != null && tr != null)
            {
                downLinetype = FindLinetype(db, tr, "DASHED")
                    ?? FindLinetype(db, tr, "HIDDEN");
            }

            void ApplyVerticalLinetype(Entity entity)
            {
                if (m.VerticalDir != VerticalDirection.Down)
                    return;

                if (string.IsNullOrEmpty(downLinetype))
                    return;

                entity.Linetype = downLinetype;
                entity.LinetypeScale = 1.0;
            }

            // Vẽ phần khung ống đi tới (Nét liền)
            if (Ls > Eps)
            {
                var solidRect = new Polyline();
                solidRect.Elevation = z;
                solidRect.AddVertexAt(0, pt(0, -h), 0, 0, 0);
                solidRect.AddVertexAt(1, pt(Ls, -h), 0, 0, 0);
                solidRect.AddVertexAt(2, pt(Ls, h), 0, 0, 0);
                solidRect.AddVertexAt(3, pt(0, h), 0, 0, 0);
                solidRect.Closed = true;
                result.Add(solidRect);
            }
            else
            {
                // Nếu Ls = 0, thì ít nhất cũng phải có đường nối ở miệng ống vào
                result.Add(new Line(To3(pt(0, -h), z), To3(pt(0, h), z)));
            }

            // Vẽ phần khung hình vuông (hoặc chữ nhật) chứa dấu X (Nét đứt nếu là Down)
            var dropRect = new Polyline();
            dropRect.Elevation = z;
            dropRect.AddVertexAt(0, pt(Ls, -h), 0, 0, 0);
            dropRect.AddVertexAt(1, pt(Lf, -h), 0, 0, 0);
            dropRect.AddVertexAt(2, pt(Lf, h), 0, 0, 0);
            dropRect.AddVertexAt(3, pt(Ls, h), 0, 0, 0);
            dropRect.Closed = true;

            ApplyVerticalLinetype(dropRect);
            result.Add(dropRect);

            // Vẽ Dấu X (Nét đứt nếu là Down)
            Line x1 = new Line( To3(pt(Ls, -h), z), To3(pt(Lf, h), z));

            Line x2 = new Line( To3(pt(Ls, h), z), To3(pt(Lf, -h), z));

            ApplyVerticalLinetype(x1);
            ApplyVerticalLinetype(x2);

            result.Add(x1);
            result.Add(x2);

            return result;
        }

        private static Point3d To3(Point2d p, double z) { return new Point3d(p.X, p.Y, z); }

        private static DBText MakeText(Point3d pos, string text, double height)
        {
            var t = new DBText();
            t.Position = pos;
            t.Height = height <= 0 ? 50 : height;
            t.TextString = text;
            return t;
        }

        private static string FindLinetype(Database db, Transaction tr, string name)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            return ltt.Has(name) ? name : null;
        }

        private static void EnsureLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;

            lt.UpgradeOpen();
            var rec = new LayerTableRecord();
            rec.Name = name;
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void CreateGroup(Database db, Transaction tr, List<ObjectId> ids)
        {
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

            int i = 1;
            string name;
            do { name = DuctElbowSettings.GroupPrefix + i++; } while (gd.Contains(name));

            var g = new Group(name, true);
            gd.SetAt(name, g);
            tr.AddNewlyCreatedDBObject(g, true);
            foreach (ObjectId id in ids) g.Append(id);
        }

        public static List<Entity> BuildPreviewEntities(DuctElbowModel model)
        {
            return model.Type == DuctElbowType.Vertical
                ? BuildVerticalElbow(null, null, model)
                : BuildElbow(model);
        }
    }
}