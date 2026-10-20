using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class DuctBranchHelper
    {
        private const double Eps = 1e-6;
        private const double Quarter = Math.PI / 2.0;   // 90 độ

        public static Polyline CreateOutline(DuctBranchModel m)
        {
            Outline outline;
            if (m.Type == DuctBranchType.EEX) outline = BuildEEX(m);
            else if (m.Type == DuctBranchType.EEY) outline = BuildEEY(m);
            else outline = BuildEEZ(m);

            return outline.ToPolyline(m);
        }

        public static void Draw(Database db, Transaction tr, DuctBranchModel m)
        {
            EnsureLayer(db, tr, m.LayerName);

            Polyline pl = CreateOutline(m);
            pl.Layer = m.LayerName;

            var modelSpace = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ObjectId id = modelSpace.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            CreateGroup(db, tr, new List<ObjectId> { id });
        }

        private static Outline BuildEEX(DuctBranchModel m)
        {
            double hw = m.MainWidth / 2;
            int side = m.BranchSide == ElbowSide.Left ? 1 : -1;

            // Ống đi thẳng sát thành đối diện nên thành phía nhánh của nó cách trục (Ws - hw) về phía nhánh.
            double wallDist = m.ThroughWidth - hw;

            Outline branch = MakeBranch(side, hw, m.MainHeight, m.Branch1Width, m.Branch1Radius, m.BranchHeight, wallDist);
            double mouthA = m.MainHeight + m.Branch1Radius + m.Branch1Width / 2 + m.ThroughHeight;

            var o = new Outline();
            o.Start(0, -side * hw);
            o.LineTo(0, side * hw);              // miệng vào
            o.Append(branch);                    // thành phía nhánh: chạy hết nhánh cong
            o.LineTo(mouthA, side * wallDist);   // dọc thành ống đi thẳng
            o.LineTo(mouthA, -side * hw);        // miệng ống đi thẳng
            return o;                            // đóng lại về điểm đầu = thành đối diện
        }

        private static Outline BuildEEZ(DuctBranchModel m)
        {
            double hw = m.MainWidth / 2;
            double wallDist = m.ThroughWidth / 2;      // thành ống đi thẳng cách trục Ws/2 về hai phía

            Outline left = MakeBranch(+1, hw, m.MainHeight, m.Branch1Width, m.Branch1Radius, m.BranchHeight, wallDist);
            Outline right = MakeBranch(-1, hw, m.MainHeight, m.Branch2Width, m.Branch2Radius, m.BranchHeight, wallDist);

            double reach = Math.Max(m.Branch1Radius + m.Branch1Width / 2, m.Branch2Radius + m.Branch2Width / 2);
            double mouthA = m.MainHeight + reach + m.ThroughHeight;

            var o = new Outline();
            o.Start(0, -hw);
            o.LineTo(0, hw);                     // miệng vào
            o.Append(left);                      // nhánh trái, đi tới thành ống đi thẳng
            o.LineTo(mouthA, wallDist);
            o.LineTo(mouthA, -wallDist);         // miệng ống đi thẳng
            o.AppendReversed(right);             // nhánh phải, đi ngược từ thành ống đi thẳng về miệng vào
            return o;
        }

        private static Outline BuildEEY(DuctBranchModel m)
        {
            double hw = m.MainWidth / 2;

            // Wb > W/2: hai cung ngoài giao nhau tại đỉnh V trên trục tim -> "thành chặn" chính là trục (cách 0).
            // Wb <= W/2: cung ngoài vẽ đủ 90°, thành chặn rơi đúng điểm bắt đầu của cung (hw - Wb);
            //            đoạn nối giữa hai cung chính là line đóng (dài W - 2Wb, bằng 0 khi Wb = W/2).
            double wallDist = Math.Max(hw - m.Branch1Width, 0);

            Outline left = MakeBranch(+1, hw, m.MainHeight, m.Branch1Width, m.Branch1Radius, m.BranchHeight, wallDist);
            Outline right = MakeBranch(-1, hw, m.MainHeight, m.Branch1Width, m.Branch1Radius, m.BranchHeight, wallDist);

            var o = new Outline();
            o.Start(0, -hw);
            o.LineTo(0, hw);                     // miệng vào
            o.Append(left);
            o.AppendReversed(right);
            return o;
        }

        private static Outline MakeBranch(int side, double hw, double aE, double wb, double centerRadius, double hb, double wallDist)
        {
            double rIn = centerRadius - wb / 2;      // bán kính cung trong
            double rOut = rIn + wb;                  // bán kính cung ngoài
            double bMid = side * (hw + rIn);         // b của đường chứa miệng ra (tâm cung nằm ở (aE, bMid))

            double cosAlpha = (hw + rIn - wallDist) / rOut;
            bool touchesWall = cosAlpha < 1 - 1e-9;
            double alpha = touchesWall ? Math.Acos(Math.Max(-1, cosAlpha)) : 0;

            var o = new Outline();
            o.Start(aE, side * hw);
            o.ArcTo(aE + rIn, bMid, side * Quarter);                    // cung trong
            o.LineTo(aE + rIn, bMid + side * hb);                       // thành trong của đoạn thẳng nhánh
            o.LineTo(aE + rOut, bMid + side * hb);                      // miệng ra
            o.LineTo(aE + rOut, bMid);                                  // thành ngoài của đoạn thẳng nhánh
            o.ArcTo(aE + rOut * Math.Sin(alpha),                        // cung ngoài, đi ngược về điểm chạm thành
                    side * (hw + rIn - rOut * Math.Cos(alpha)),
                    -side * (Quarter - alpha));

            bool needStep = !touchesWall && (hw - wb) - wallDist > Eps;
            if (needStep) o.LineTo(aE, side * wallDist);                // đoạn phẳng nối vào thành
            return o;
        }

        private class Outline
        {
            private class Vertex
            {
                public double A, B, Bulge;
            }

            private readonly List<Vertex> _vertices = new List<Vertex>();

            public void Start(double a, double b)
            {
                _vertices.Add(new Vertex { A = a, B = b });
            }

            public void LineTo(double a, double b)
            {
                Add(a, b, 0);
            }

            public void ArcTo(double a, double b, double sweep)
            {
                Add(a, b, Math.Tan(sweep / 4));
            }

            /// <summary>Nối tiếp một đường viền khác theo chiều thuận.</summary>
            public void Append(Outline other)
            {
                for (int i = 0; i < other._vertices.Count; i++)
                {
                    Vertex v = other._vertices[i];
                    double bulge = i == 0 ? 0 : other._vertices[i - 1].Bulge;
                    Add(v.A, v.B, bulge);
                }
            }

            /// <summary>Nối tiếp một đường viền khác theo chiều NGƯỢC (đảo chiều cung thì đổi dấu bulge).</summary>
            public void AppendReversed(Outline other)
            {
                int last = other._vertices.Count - 1;
                for (int i = last; i >= 0; i--)
                {
                    Vertex v = other._vertices[i];
                    double bulge = i == last ? 0 : -v.Bulge;
                    Add(v.A, v.B, bulge);
                }
            }

            // Thêm đỉnh mới; bulge là của đoạn đi từ đỉnh trước tới đỉnh này. Đỉnh trùng đỉnh trước thì bỏ qua.
            private void Add(double a, double b, double bulgeFromPrevious)
            {
                Vertex previous = _vertices[_vertices.Count - 1];
                if (Math.Abs(previous.A - a) < Eps && Math.Abs(previous.B - b) < Eps) return;

                previous.Bulge = bulgeFromPrevious;
                _vertices.Add(new Vertex { A = a, B = b });
            }

            /// <summary>Đổi sang toạ độ bản vẽ và tạo Polyline kín.</summary>
            public Polyline ToPolyline(DuctBranchModel m)
            {
                Point2d origin = new Point2d(m.InsertPoint.X, m.InsertPoint.Y);
                Vector2d along = new Vector2d(m.Direction.X, m.Direction.Y).GetNormal();   // trục a
                Vector2d across = along.RotateBy(Math.PI / 2);                              // trục b (bên trái)

                // Đỉnh cuối trùng đỉnh đầu thì bỏ (Polyline kín tự nối về đỉnh đầu)
                int count = _vertices.Count;
                Vertex first = _vertices[0];
                Vertex lastVertex = _vertices[count - 1];
                if (count > 1 && Math.Abs(lastVertex.A - first.A) < Eps && Math.Abs(lastVertex.B - first.B) < Eps) count--;

                var pl = new Polyline();
                pl.Elevation = m.InsertPoint.Z;
                for (int i = 0; i < count; i++)
                {
                    Vertex v = _vertices[i];
                    Point2d p = origin + along * v.A + across * v.B;
                    pl.AddVertexAt(i, p, v.Bulge, 0, 0);
                }
                pl.Closed = true;
                return pl;
            }
        }

        private static void EnsureLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) return;

            layers.UpgradeOpen();
            var layer = new LayerTableRecord();
            layer.Name = name;
            layers.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        private static void CreateGroup(Database db, Transaction tr, List<ObjectId> ids)
        {
            var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

            int number = 1;
            string name;
            do { name = DuctBranchSettings.GroupPrefix + number++; } while (groups.Contains(name));

            var group = new Group(name, true);
            groups.SetAt(name, group);
            tr.AddNewlyCreatedDBObject(group, true);
            foreach (ObjectId id in ids) group.Append(id);
        }
    }
}