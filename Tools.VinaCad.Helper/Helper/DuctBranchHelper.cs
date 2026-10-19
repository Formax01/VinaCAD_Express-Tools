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
        private const double HalfPi = Math.PI / 2.0;

        public static List<ObjectId> Draw(Database db, Transaction tr, DuctBranchModel m)
        {
            List<Entity> ents = Build(m);
            EnsureLayer(db, tr, m.LayerName);

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            var ids = new List<ObjectId>();
            foreach (Entity e in ents)
            {
                if (!string.IsNullOrEmpty(m.LayerName)) e.Layer = m.LayerName;
                ids.Add(ms.AppendEntity(e));
                tr.AddNewlyCreatedDBObject(e, true);
            }

            CreateGroup(db, tr, ids);
            return ids;
        }

        public static List<Entity> BuildPreviewEntities(DuctBranchModel m)
        {
            return Build(m);
        }

        public static List<Entity> Build(DuctBranchModel m)
        {
            var list = new List<Entity>();
            var f = new Frame(m);
            switch (m.Type)
            {
                case DuctBranchType.EEX: BuildEEX(list, f, m); break;
                case DuctBranchType.EEY: BuildEEY(list, f, m); break;
                default: BuildEEZ(list, f, m); break;
            }
            return list;
        }

        private static void BuildEEX(List<Entity> list, Frame f, DuctBranchModel m)
        {
            double hw = m.MainWidth / 2.0;
            double wb = m.Branch1Width;
            double rIn = m.Branch1CenterRadius - wb / 2.0;
            double aE = m.MainHeight;
            int s = m.BranchSide == ElbowSide.Left ? 1 : -1;

            // Ống đi thẳng sát thành đối diện nhánh, rộng Ws -> thành phía nhánh cách trục (Ws - hw) về phía nhánh.
            double dw = m.ThroughWidth - hw;

            Elbow e = MakeElbow(s, aE, hw, wb, rIn, dw, m.BranchHeight);
            double lBody = aE + e.ROut + m.ThroughHeight;

            var p = new Outline(f);
            p.MoveTo(0, -s * hw);
            p.LineTo(0, s * hw);                    // miệng vào
            ElbowForward(p, e);                     // thành phía nhánh: cút cong
            p.LineTo(lBody, s * dw);
            p.LineTo(lBody, -s * hw);               // miệng ra đoạn thẳng (sát thành đối diện)
            list.Add(p.ToPolyline());
        }

        private static void BuildEEZ(List<Entity> list, Frame f, DuctBranchModel m)
        {
            double hw = m.MainWidth / 2.0;
            double aE = m.MainHeight;

            double wb1 = m.Branch1Width, wb2 = m.Branch2Width;
            double rIn1 = m.Branch1CenterRadius - wb1 / 2.0;
            double rIn2 = m.Branch2CenterRadius - wb2 / 2.0;

            // Ống đi thẳng ở giữa rộng Ws: cung ngoài bị cắt bởi hai thành của nó (cách trục Ws/2)
            double hs = m.ThroughWidth / 2.0;
            Elbow eL = MakeElbow(+1, aE, hw, wb1, rIn1, hs, m.BranchHeight);   // nhánh 1 = bên trái
            Elbow eR = MakeElbow(-1, aE, hw, wb2, rIn2, hs, m.BranchHeight);   // nhánh 2 = bên phải

            double lBody = aE + Math.Max(eL.ROut, eR.ROut) + m.ThroughHeight;

            var p = new Outline(f);
            p.MoveTo(0, -hw);
            p.LineTo(0, hw);                        // miệng vào
            ElbowForward(p, eL);
            p.LineTo(lBody, hs);
            p.LineTo(lBody, -hs);                   // miệng ra đoạn thẳng
            ElbowBackward(p, eR);
            p.LineTo(0, -hw);
            list.Add(p.ToPolyline());
        }

        private static void BuildEEY(List<Entity> list, Frame f, DuctBranchModel m)
        {
            double hw = m.MainWidth / 2.0;
            double wb = m.Branch1Width;
            double rIn = m.Branch1CenterRadius - wb / 2.0;
            double aE = m.MainHeight;

            // Wb > W/2: hai miệng nhánh chồng nhau, hai cung ngoài giao nhau tại đỉnh V trên trục tim -> cắt tại b = 0.
            // Wb <= W/2: cung ngoài vẽ đủ 90°, giữa hai cung có line đóng (dài W - 2Wb, bằng 0 khi Wb = W/2).
            double? cutAt = wb > hw + Eps ? (double?)0.0 : null;

            Elbow eL = MakeElbow(+1, aE, hw, wb, rIn, cutAt, m.BranchHeight);
            Elbow eR = MakeElbow(-1, aE, hw, wb, rIn, cutAt, m.BranchHeight);

            var p = new Outline(f);
            p.MoveTo(0, -hw);
            p.LineTo(0, hw);                        // miệng vào
            p.LineTo(aE, hw);                       // thành trái
            ElbowForward(p, eL);
            ElbowBackward(p, eR);                   // tự nối line đóng giữa 2 cung ngoài nếu Wb < W/2
            p.LineTo(aE, -hw);
            p.LineTo(0, -hw);
            list.Add(p.ToPolyline());
        }

        private sealed class Elbow
        {
            public int S;               // +1 trái, -1 phải
            public double AE;           // toạ độ a của miệng vào cút
            public double BIn;          // khoảng cách từ trục tim tới thành TRONG miệng vào (đo về phía S)
            public double Wb, RIn, ROut;
            public double Ext;          // chiều cao ống rẽ = đoạn thẳng nối dài sau miệng ra của cung
            public double Alpha0;       // góc cắt của cung ngoài (0 = vẽ đủ 90°)
            public double DW;           // vị trí thành chặn (đo về phía S)
            public bool Shelf;          // cung ngoài không chạm thành chặn -> nối bậc phẳng tại a = AE
        }

        private static Elbow MakeElbow(int s, double aE, double bIn, double wb, double rIn, double? cutAt, double ext)
        {
            var e = new Elbow { S = s, AE = aE, BIn = bIn, Wb = wb, RIn = rIn, ROut = rIn + wb, Ext = ext };
            if (cutAt.HasValue)
            {
                double cosA = (bIn + rIn - cutAt.Value) / e.ROut;
                e.DW = cutAt.Value;
                if (cosA >= 1.0 - 1e-9)
                {
                    e.Alpha0 = 0.0;
                    e.Shelf = (bIn - wb) - cutAt.Value > Eps;
                }
                else
                {
                    e.Alpha0 = Math.Acos(Math.Max(-1.0, cosA));
                }
            }
            return e;
        }

        private static double CutA(Elbow e) { return e.AE + e.ROut * Math.Sin(e.Alpha0); }
        private static double CutB(Elbow e) { return e.S * (e.BIn + e.RIn - e.ROut * Math.Cos(e.Alpha0)); }

        private static void ElbowForward(Outline p, Elbow e)
        {
            double s = e.S;
            double cb = s * (e.BIn + e.RIn);
            p.LineTo(e.AE, s * e.BIn);                                    // thành ống chính tới chỗ bắt đầu cung trong
            p.ArcTo(e.AE + e.RIn, cb, s * HalfPi);                        // thành trong
            p.LineTo(e.AE + e.RIn, cb + s * e.Ext);                       // thành trong đoạn thẳng của nhánh
            p.LineTo(e.AE + e.ROut, cb + s * e.Ext);                      // miệng ra của nhánh
            p.LineTo(e.AE + e.ROut, cb);                                  // thành ngoài đoạn thẳng của nhánh
            p.ArcTo(CutA(e), CutB(e), -s * (HalfPi - e.Alpha0));          // thành ngoài, đi ngược về điểm cắt
            if (e.Shelf) p.LineTo(e.AE, s * e.DW);                        // bậc phẳng nối vào thành ống đi thẳng
        }

        private static void ElbowBackward(Outline p, Elbow e)
        {
            double s = e.S;
            double cb = s * (e.BIn + e.RIn);
            if (e.Shelf) p.LineTo(e.AE, s * e.DW);
            p.LineTo(CutA(e), CutB(e));
            p.ArcTo(e.AE + e.ROut, cb, s * (HalfPi - e.Alpha0));
            p.LineTo(e.AE + e.ROut, cb + s * e.Ext);
            p.LineTo(e.AE + e.RIn, cb + s * e.Ext);
            p.LineTo(e.AE + e.RIn, cb);
            p.ArcTo(e.AE, s * e.BIn, -s * HalfPi);
        }

        private sealed class Frame
        {
            public readonly Point2d O;
            public readonly Vector2d D;
            public readonly Vector2d N;
            public readonly double Z;

            public Frame(DuctBranchModel m)
            {
                O = new Point2d(m.InsertPoint.X, m.InsertPoint.Y);
                D = new Vector2d(m.Direction.X, m.Direction.Y).GetNormal();
                N = D.RotateBy(Math.PI / 2.0);
                Z = m.InsertPoint.Z;
            }

            public Point2d P2(double a, double b) { return O + D * a + N * b; }
        }

        private sealed class Outline
        {
            private readonly Frame _f;
            private readonly List<Point2d> _pts = new List<Point2d>();
            private readonly List<double> _bulges = new List<double>();

            public Outline(Frame f) { _f = f; }

            public void MoveTo(double a, double b)
            {
                _pts.Add(_f.P2(a, b));
                _bulges.Add(0.0);
            }

            public void LineTo(double a, double b)
            {
                Point2d p = _f.P2(a, b);
                int last = _pts.Count - 1;
                if (p.GetDistanceTo(_pts[last]) < Eps) return;
                _bulges[last] = 0.0;
                _pts.Add(p);
                _bulges.Add(0.0);
            }

            public void ArcTo(double a, double b, double sweep)
            {
                Point2d p = _f.P2(a, b);
                int last = _pts.Count - 1;
                if (p.GetDistanceTo(_pts[last]) < Eps) return;
                _bulges[last] = Math.Abs(sweep) < 1e-9 ? 0.0 : Math.Tan(sweep / 4.0);
                _pts.Add(p);
                _bulges.Add(0.0);
            }

            public Polyline ToPolyline()
            {
                int n = _pts.Count;
                if (n > 1 && _pts[n - 1].GetDistanceTo(_pts[0]) < Eps) n--;

                var pl = new Polyline();
                pl.Elevation = _f.Z;
                for (int i = 0; i < n; i++)
                    pl.AddVertexAt(i, _pts[i], _bulges[i], 0, 0);
                pl.Closed = true;
                return pl;
            }
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
            do { name = DuctBranchSettings.GroupPrefix + i++; } while (gd.Contains(name));

            var g = new Group(name, true);
            gd.SetAt(name, g);
            tr.AddNewlyCreatedDBObject(g, true);
            foreach (ObjectId id in ids) g.Append(id);
        }
    }
}