using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{
    public class TextHelper
    {
        public static Dictionary<string, string> GetTextProperties(Database db, ObjectId textId)
        {
            Dictionary<string, string> properties = new Dictionary<string, string>();

            if (db == null || textId.IsNull || !textId.IsValid)
                return properties;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(textId, OpenMode.ForRead) as Entity;

                if (ent == null)
                    return properties;

                if (ent is DBText dbText)
                {
                    properties["TEXT"] = dbText.TextString;
                    properties["LAYER"] = dbText.Layer;
                    properties["HEIGHT"] = dbText.Height.ToString();
                    properties["STYLE"] = dbText.TextStyleName;
                    properties["SCALE"] = dbText.LinetypeScale.ToString();
                }
                else if (ent is MText mText)
                {
                    properties["TEXT"] = mText.Contents;
                    properties["LAYER"] = mText.Layer;
                    properties["HEIGHT"] = mText.TextHeight.ToString();
                    properties["STYLE"] = mText.TextStyleName;
                    properties["SCALE"] = mText.LinetypeScale.ToString();
                }

                tr.Commit();
            }

            return properties;
        }

        public static List<ObjectId> PickTexts(Database db, Editor ed)
        {
            List<ObjectId> resultIds = new List<ObjectId>();

            if (db == null || ed == null)
                return resultIds;

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn text: ";
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return resultIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    Entity ent = tr.GetObject(
                        selectedObj.ObjectId,
                        OpenMode.ForRead) as Entity;

                    if (ent is DBText || ent is MText)
                    {
                        resultIds.Add(selectedObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            if (resultIds.Any())
            {
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.SetImpliedSelection(resultIds.ToArray());
            }

            ed.UpdateScreen();

            return resultIds;
        }

        public static void SelectAndZoom(Database db, Editor ed, ObjectId[] objectIds)
        {
            if (db == null || ed == null || objectIds == null || objectIds.Length == 0)
                return;

            SelectIds(ed, objectIds);
            ZoomToObjects(db, ed, objectIds);
        }

        public static void SelectIds(Editor ed, ObjectId[] objectIds)
        {
            if (ed == null || objectIds == null || objectIds.Length == 0)
                return;

            ObjectId[] validIds = objectIds
                .Where(x => !x.IsNull && x.IsValid)
                .Distinct()
                .ToArray();

            if (validIds.Length == 0)
                return;

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.SetImpliedSelection(validIds);

            ed.WriteMessage($"\nĐã chọn {validIds.Length} đối tượng text.");
            ed.UpdateScreen();
        }

        public static void ZoomToObjects(Database db, Editor ed, ObjectId[] objectIds)
        {
            if (db == null || ed == null || objectIds == null || objectIds.Length == 0)
                return;

            Extents3d? totalExtents = null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in objectIds)
                {
                    if (id.IsNull || !id.IsValid)
                        continue;

                    if (id.Database != db)
                        continue;

                    Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;

                    if (ent == null)
                        continue;

                    try
                    {
                        Extents3d ext = ent.GeometricExtents;

                        if (totalExtents == null)
                        {
                            totalExtents = ext;
                        }
                        else
                        {
                            Extents3d temp = totalExtents.Value;
                            temp.AddExtents(ext);
                            totalExtents = temp;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                tr.Commit();
            }

            if (totalExtents == null)
                return;

            Extents3d ex = totalExtents.Value;

            Point3d min = ex.MinPoint;
            Point3d max = ex.MaxPoint;

            Point3d center3d = new Point3d(
                (min.X + max.X) / 2.0,
                (min.Y + max.Y) / 2.0,
                (min.Z + max.Z) / 2.0);

            double width = Math.Max((max.X - min.X) * 1.5, 1000);
            double height = Math.Max((max.Y - min.Y) * 1.5, 1000);

            using (ViewTableRecord view = ed.GetCurrentView())
            {
                view.Target = center3d;
                view.CenterPoint = new Point2d(0, 0);
                view.Width = width;
                view.Height = height;

                ed.SetCurrentView(view);
            }

            ed.Regen();
        }

        public static void ClearSelection(Editor ed)
        {
            if (ed == null)
                return;

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();
        }
    }
}
