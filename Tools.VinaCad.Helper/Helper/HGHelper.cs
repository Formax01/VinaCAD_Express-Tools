using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper
{
    public static class HGHelper
    {
        public const double LineLength = 50.0;
        public const double Pitch = 100.0;      // điểm đặt kế tiếp = đỉnh cung trước
        private const double Radius = 62.5;
        private const double CenterX = 37.5;    // 50 + 50 - 62.5

        /// <summary>
        /// Dựng các entity xếp liên tiếp dọc trục X cục bộ, bắt đầu từ (0,0).
        /// Nơi gọi chịu trách nhiệm Dispose nếu không add vào database.
        /// </summary>
        public static List<Entity> BuildEntities(double length)
        {
            List<Entity> entities = new List<Entity>();
            if (length < LineLength - 1e-9)
                return entities;

            int n = (int)Math.Floor((length - LineLength) / Pitch + 1e-9) + 1;
            double half = Math.Acos(CenterX / Radius);   // 53.13° (rad)

            for (int i = 0; i < n; i++)
            {
                double ox = i * Pitch;
                entities.Add(new Line(new Point3d(ox, 0, 0), new Point3d(ox + LineLength, 0, 0)));
                entities.Add(new Arc(new Point3d(ox + CenterX, 0, 0), Radius, -half, half));
            }
            return entities;
        }

        /// <summary>
        /// Tạo block definition (các đối tượng xếp dọc trục X cục bộ) rồi chèn
        /// BlockReference tại 'start' (WCS) với góc 'rotation' (WCS).
        /// Trả về ObjectId của reference, hoặc ObjectId.Null nếu lỗi.
        /// </summary>
        public static ObjectId CreateBlockReference(Database db, Point3d start, double length, double rotation)
        {
            if (db == null)
                return ObjectId.Null;

            List<Entity> entities = BuildEntities(length);
            if (entities.Count == 0)
                return ObjectId.Null;

            string blockName = "HG_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

                    // 1) Block definition, gốc tại (0,0) = điểm đầu => tâm quay
                    BlockTableRecord def = new BlockTableRecord
                    {
                        Name = blockName,
                        Origin = Point3d.Origin
                    };
                    ObjectId defId = bt.Add(def);
                    tr.AddNewlyCreatedDBObject(def, true);

                    foreach (Entity ent in entities)
                    {
                        def.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                    }

                    // 2) Block reference trong không gian hiện hành
                    BlockTableRecord space =
                        (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    BlockReference br = new BlockReference(start, defId);
                    br.SetDatabaseDefaults(db);
                    br.Rotation = rotation;

                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    tr.Commit();
                    return br.ObjectId;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Không thể tạo block HG trong database.", ex);
            }
        }
    }
}