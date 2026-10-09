using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Prima.VinaCAD.EditorInput;

namespace Tools.VinaCad.Helper
{
    public static class BanisterHelper
    {
        public static void DrawIllustrations(Canvas slopedCanvas, Canvas flatCanvas)
        {
            var sample = new BanisterInput();
            DrawRailing(slopedCanvas, sample, 2400, 0.5);
            DrawRailing(flatCanvas, sample, 2400, 0.0);
        }

        private static void DrawRailing(Canvas canvas, BanisterInput input, double lengthMm, double slope)
        {
            const double px = 0.065;
            const double left = 12;
            const double bottom = 8;

            // Sửa lỗi CS0104: Chỉ định rõ dùng Color của System.Windows.Media
            var stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0xA3, 0xB5));
            var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0x1F, 0xA3, 0xB5));

            Point Q(double x, double xRef, double off) =>
                new Point(left + x * px, canvas.Height - bottom - (slope * xRef + off) * px);

            canvas.Children.Clear();
            foreach (var r in BanisterGeometry.Build(input, lengthMm, slope))
            {
                double x0 = r.X, x1 = r.X + r.W, y0 = r.Y, y1 = r.Y + r.H;
                PointCollection points;
                if (r.Cap > 0)
                {
                    double c = r.Cap;
                    points = new PointCollection
                    {
                        Q(x0 - c, x0, y0), Q(x0, x0, y0), Q(x1, x1, y0), Q(x1 + c, x1, y0),
                        Q(x1 + c, x1, y1), Q(x1, x1, y1), Q(x0, x0, y1), Q(x0 - c, x0, y1)
                    };
                }
                else
                {
                    points = new PointCollection
                    {
                        Q(x0, x0, y0), Q(x1, x1, y0), Q(x1, x1, y1), Q(x0, x0, y1)
                    };
                }

                canvas.Children.Add(new Polygon
                {
                    Points = points,
                    Stroke = stroke,
                    StrokeThickness = 0.7,
                    Fill = fill
                });
            }
        }

        // Nếu đường nối lệch dưới ~0,5 độ so với phương ngang thì coi là ngang
        public static Point3d NormalizeEnd(Point3d p1, Point3d p2)
        {
            double absDx = Math.Abs(p2.X - p1.X);
            if (absDx > 0 && Math.Abs(p2.Y - p1.Y) / absDx < 0.01)
                return new Point3d(p2.X, p1.Y, p2.Z);
            return p2;
        }

        public static ObjectIdCollection CreateBanister(Database db, BanisterInput input,
                                                        Point3d p1, Point3d p2)
        {
            var ids = new ObjectIdCollection();
            double s = GetUnitScale(db, input.DrawingUnit);

            double dx = p2.X - p1.X;
            double sign = dx >= 0 ? 1 : -1;
            double absDx = Math.Abs(dx);
            double L = absDx / s;                                   // chiều dài ngang theo mm
            if (L < 1) return ids;

            double m = (p2.Y - p1.Y) / absDx;                       // độ dốc theo chiều đi
            if (Math.Abs(m) > 2.75) return ids;                     // dốc quá ~70 độ thì bỏ qua
            //double k = Math.Sqrt(1 + m * m);                       

            var rects = BanisterGeometry.Build(input, L, m);
            if (rects.Count > 3000)
                throw new InvalidOperationException(
                    $"Lan can có quá nhiều chi tiết ({rects.Count}). Hãy kiểm tra đơn vị bản vẽ và khoảng cách hai điểm đã chọn.");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, input.LayerName, input.ColorIndex);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Nơi chứa các Polyline: Model/Paper space, hoặc một block ẩn danh
                BlockTableRecord container = space;
                BlockTableRecord anonBlock = null;

                if (input.ToGroup)
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    anonBlock = new BlockTableRecord
                    {
                        Name = "*U",        // block ẩn danh, hệ thống tự đánh số *U1, *U2, ...
                        Origin = p1         // điểm gốc của block = điểm đầu lan can
                    };
                    bt.Add(anonBlock);
                    tr.AddNewlyCreatedDBObject(anonBlock, true);
                    container = anonBlock;
                }

                foreach (var r in rects)
                {
                    double xl0 = r.X, xl1 = r.X + r.W;                   // tọa độ ngang cục bộ (mm)

                    // Sửa lỗi CS0104: Chỉ định rõ dùng Polyline của Teigha.DatabaseServices
                    var pl = new Teigha.DatabaseServices.Polyline();

                    if (r.Cap > 0)
                    {
                        // Tay vịn nghiêng liền khối: đầu ngang trái -> đoạn dốc -> đầu ngang phải (8 đỉnh)
                        double c = r.Cap;
                        var local = new (double x, double y)[]
                        {
                            (xl0 - c, m * xl0 + r.Y),
                            (xl0,     m * xl0 + r.Y),
                            (xl1,     m * xl1 + r.Y),
                            (xl1 + c, m * xl1 + r.Y),
                            (xl1 + c, m * xl1 + r.Y + r.H),
                            (xl1,     m * xl1 + r.Y + r.H),
                            (xl0,     m * xl0 + r.Y + r.H),
                            (xl0 - c, m * xl0 + r.Y + r.H)
                        };
                        for (int v = 0; v < local.Length; v++)
                            pl.AddVertexAt(v,
                                new Point2d(p1.X + sign * local[v].x * s, p1.Y + local[v].y * s), 0, 0, 0);
                    }
                    else
                    {
                        // Chi tiết thường: hình chữ nhật/bình hành theo dốc
                        double xa = p1.X + sign * xl0 * s;
                        double xb = p1.X + sign * xl1 * s;

                        double yB0 = p1.Y + (m * xl0 + r.Y) * s;
                        double yB1 = p1.Y + (m * xl1 + r.Y) * s;
                        double yT0 = p1.Y + (m * xl0 + r.Y + r.H) * s;
                        double yT1 = p1.Y + (m * xl1 + r.Y + r.H) * s;

                        pl.AddVertexAt(0, new Point2d(xa, yB0), 0, 0, 0);
                        pl.AddVertexAt(1, new Point2d(xb, yB1), 0, 0, 0);
                        pl.AddVertexAt(2, new Point2d(xb, yT1), 0, 0, 0);
                        pl.AddVertexAt(3, new Point2d(xa, yT0), 0, 0, 0);
                    }

                    pl.Closed = true;
                    pl.Layer = input.LayerName;

                    // Sửa lỗi CS0104: Chỉ định rõ dùng Color của Teigha.Colors
                    pl.Color = Teigha.Colors.Color.FromColorIndex(ColorMethod.ByAci, input.ColorIndex);

                    container.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    if (!input.ToGroup) ids.Add(pl.ObjectId);
                }
                if (anonBlock != null)
                {
                    // Chèn block tại đúng điểm gốc nên hình nằm đúng vị trí đã chọn
                    var br = new BlockReference(p1, anonBlock.ObjectId) { Layer = input.LayerName };
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    ids.Add(br.ObjectId);
                }

                tr.Commit();
            }
            return ids;
        }
        // Chọn một đối tượng, trả về layer của nó và màu của layer đó
        public static bool TryPickLayer(Editor ed, Database db, out string layerName, out short aci)
        {
            layerName = null;
            aci = 7;

            var res = ed.GetEntity(new PromptEntityOptions("\nChọn đối tượng để lấy layer: "));
            if (res.Status != PromptStatus.OK) return false;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead);
                layerName = ent.Layer;

                if (layerName.Contains('|'))
                {
                    ed.WriteMessage("\nĐối tượng thuộc xref, không dùng được layer này.");
                    layerName = null;
                    return false;
                }

                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead);
                short c = rec.Color.ColorIndex;
                aci = (c >= 1 && c <= 255) ? c : (short)7;      // màu True Color thì lấy tạm 7

                tr.Commit();
            }
            return true;
        }

        public static Dictionary<string, short> GetLayerColors(Database db)
        {
            var d = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var r = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (r.Name.Contains('|')) continue;                  // bỏ layer của xref
                    short aci = r.Color.ColorIndex;
                    d[r.Name] = (aci >= 1 && aci <= 255) ? aci : (short)7;
                }
                tr.Commit();
            }
            return d;
        }

        public static double GetUnitScale(Database db, string unit)
        {
            switch (unit)
            {
                case "mm": return 1.0;
                case "cm": return 0.1;
                case "m": return 0.001;
            }

            // "Auto": đọc INSUNITS của bản vẽ. Hệ số = (đơn vị bản vẽ) trên mỗi mm
            switch (db.Insunits)
            {
                case UnitsValue.Millimeters: return 1.0;
                case UnitsValue.Centimeters: return 0.1;
                case UnitsValue.Decimeters: return 0.01;
                case UnitsValue.Meters: return 0.001;
                case UnitsValue.Kilometers: return 0.000001;
                case UnitsValue.Inches: return 1.0 / 25.4;
                case UnitsValue.Feet: return 1.0 / 304.8;
                default: return 1.0;      // chưa đặt hoặc không hỗ trợ: coi là mm
            }
        }
        public static string GetUnitName(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Millimeters: return "mm";
                case UnitsValue.Centimeters: return "cm";
                case UnitsValue.Decimeters: return "dm";
                case UnitsValue.Meters: return "m";
                case UnitsValue.Kilometers: return "km";
                case UnitsValue.Inches: return "inch";
                case UnitsValue.Feet: return "feet";
                default: return "chưa đặt (coi là mm)";
            }
        }

        private static void EnsureLayer(Database db, Transaction tr, string name, short aci)
        {
            
            var color = Teigha.Colors.Color.FromColorIndex(ColorMethod.ByAci, aci);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = name, Color = color };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
                return;
            }

            
        }
    }
}