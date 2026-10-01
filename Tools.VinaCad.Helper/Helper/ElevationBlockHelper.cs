using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{


    public static class ElevationBlockHelper
    {
        public static void SetSingleAttributeValue(Database db, ObjectId blockId, string newText)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference blockRef = (BlockReference)tr.GetObject(blockId, OpenMode.ForWrite);

                ObjectId attId = blockRef.AttributeCollection[0];
                AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForWrite);
                attRef.TextString = newText;

                tr.Commit();
            }
        }
       
        public static bool TryParseElevationText(string text, out string prefix, out double value)
        {
            prefix = string.Empty;
            value = 0;

            int signSymbolIndex = text.IndexOf("%%p", StringComparison.OrdinalIgnoreCase);
            if (signSymbolIndex >= 0)
            {
                prefix = text.Substring(0, signSymbolIndex);
                string numberPart = text.Substring(signSymbolIndex + 3); // bỏ qua "%%p" (3 ký tự)
                return double.TryParse(numberPart, out value);
            }

            int signIndex = text.IndexOfAny(new[] { '+', '-' });
            if (signIndex < 0)
                return false;

            prefix = text.Substring(0, signIndex);
            char signChar = text[signIndex];
            string numPart = text.Substring(signIndex + 1);

            if (!double.TryParse(numPart, out double absValue))
                return false;

            value = signChar == '-' ? -absValue : absValue;
            return true;
        }

        
        public static string BuildElevationText(string prefix, double value)
        {
            // Làm tròn trước khi so sánh với 0 — tránh sai số dấu phẩy động
            // (ví dụ phép trừ có thể ra 0.0000000001 thay vì đúng 0)
            double rounded = Math.Round(value, 3);

            if (rounded == 0)
                return $"{prefix}%%p{0:0.000}";

            string sign = rounded < 0 ? "-" : "+";
            return $"{prefix}{sign}{Math.Abs(rounded):0.000}";
        }
        public static bool IsCaoDoBlock(Database db, ObjectId objectId, out ObjectId attributeId)
        {
            attributeId = ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Điều kiện 1: phải là BlockReference (Entity thường như Line, Circle sẽ bị loại ngay)
                DBObject obj = tr.GetObject(objectId, OpenMode.ForRead);
                if (!(obj is BlockReference blockRef))
                {
                    tr.Commit();
                    return false;
                }

                // Điều kiện 1 (tiếp): phải có attribute (block attribute)
                if (blockRef.AttributeCollection.Count == 0)
                {
                    tr.Commit();
                    return false;
                }

                // Điều kiện 2: chỉ có đúng 1 attribute
                if (blockRef.AttributeCollection.Count != 1)
                {
                    tr.Commit();
                    return false;
                }

                // Điều kiện 3: giá trị attribute có chứa dấu "+"hoặc"-" hoặc "%%p"
                ObjectId attId = blockRef.AttributeCollection[0];
                AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                string text = attRef.TextString;

                bool hasSignSymbol = text.Contains("+")
                    || text.Contains("-")
                    || text.IndexOf("%%p", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!hasSignSymbol)
                {
                    tr.Commit();
                    return false;
                }

                attributeId = attId;
                tr.Commit();
                return true;
            }
        }
        // Lấy vị trí và giá trị attribute cao độ hiện tại của block
        public static double GetUnitToMeterFactor(Database db) // Lấy hệ số chuyển đổi từ đơn vị hiện tại sang mét
        {
            switch (db.Insunits)
            {
                case UnitsValue.Undefined:
                    return 1.0; 

                case UnitsValue.Millimeters:
                    return 0.001;
                case UnitsValue.Centimeters:
                    return 0.01;
                case UnitsValue.Decimeters:
                    return 0.1;
                case UnitsValue.Meters:
                    return 1.0;
                
                case UnitsValue.Hectometers:
                    return 100.0;
                case UnitsValue.Kilometers:
                    return 1000.0;
                case UnitsValue.Gigameters:
                    return 1_000_000_000.0;

                case UnitsValue.Microns:
                    return 0.000001;
                case UnitsValue.Nanometers:
                    return 0.000000001;
                case UnitsValue.Angstroms:
                    return 0.0000000001;

                case UnitsValue.Inches:
                    return 0.0254;
                case UnitsValue.Feet:
                    return 0.3048;
                case UnitsValue.Yards:
                    return 0.9144;
                case UnitsValue.Miles:
                    return 1609.344;
                case UnitsValue.Mils:
                    return 0.0000254;
                

                case UnitsValue.USSurveyInch:
                    return 0.0254000508;
                case UnitsValue.USSurveyFeet:
                    return 0.3048006096;
                case UnitsValue.USSurveyYard:
                    return 0.9144018288;
                case UnitsValue.USSurveyMile:
                    return 1609.347219;

                
                case UnitsValue.LightYears:
                    return 9_460_730_472_580_800.0;
                case UnitsValue.Parsecs:
                    return 30_856_775_814_913_673.0;

                default:
                    
                    return 1.0;
            }
        }
        public static void GetBlockInfo(Database db, ObjectId blockId, string attributeTag,
            out Point3d position, out double elevation)
        {
            position = Point3d.Origin;
            elevation = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference blockRef = (BlockReference)tr.GetObject(blockId, OpenMode.ForRead);
                position = blockRef.Position;

                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    if (attRef.Tag.Equals(attributeTag, System.StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(attRef.TextString, out elevation);
                        break;
                    }
                }

                tr.Commit();
            }
        }

        // Nhân bản block (kèm attribute) và dịch chuyển tới vị trí mới
        public static ObjectId CloneBlockWithOffset(Database db, ObjectId sourceBlockId, Vector3d offset)
        {
            ObjectIdCollection idsToClone = new ObjectIdCollection { sourceBlockId };
            ObjectId newId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference sourceRef = (BlockReference)tr.GetObject(sourceBlockId, OpenMode.ForRead);
                ObjectId ownerId = sourceRef.OwnerId; // Model Space chứa block nguồn

                IdMapping mapping = new IdMapping();
                db.DeepCloneObjects(idsToClone, ownerId, mapping, false);

                newId = mapping[sourceBlockId].Value;

                // DeepCloneObjects tự nhân bản luôn AttributeCollection vì attribute
                // là đối tượng con (owned) của BlockReference
                BlockReference newRef = (BlockReference)tr.GetObject(newId, OpenMode.ForWrite);
                newRef.TransformBy(Matrix3d.Displacement(offset));

                tr.Commit();
            }

            return newId;
        }

        
    }
}