using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;
using Teigha.Geometry;

namespace Tools.VinaCad.Action.Actions
{
    public class DTCAction
    {
        private const double POSITION_EPS = 0.0001;

        public void Execute()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                Database db = doc.Database;
                Editor ed = doc.Editor;

                // Prompt user to select pile block references via crossing/window selection
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect pile blocks (drag to create window/crossing): ";
                pso.AllowDuplicates = false;
                PromptSelectionResult psr = ed.GetSelection(pso);

                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nDTC: No blocks selected.\n");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    if (bt == null) return;

                    BlockTableRecord ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    if (ms == null) return;

                    string[] pileNames = new[] { StringDefinition.blockNamecocm, StringDefinition.blockNamecocmm };
                    var layerHints = new[] { "coc", "cọc", "pile" };

                    // Filter selected entities to identify pile candidates
                    var piles = new List<(ObjectId id, Point3d pos)>();

                    foreach (SelectedObject selObj in psr.Value)
                    {
                        DBObject obj = tr.GetObject(selObj.ObjectId, OpenMode.ForRead);
                        if (obj is BlockReference br)
                        {
                            // 1) Layer-based filter: accept block references whose layer name contains any hint.
                            string layerName = br.Layer ?? string.Empty;
                            bool layerMatch = layerHints.Any(h => layerName.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (layerMatch)
                            {
                                piles.Add((selObj.ObjectId, br.Position));
                                continue;
                            }

                            // 2) Fallback checks when layer does not match:
                            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            if (btr == null) continue;

                            bool isPileCandidate = false;

                            // 2.a) Exact configured names (keeps backward compatibility)
                            if (pileNames != null && pileNames.Any(n => !string.IsNullOrWhiteSpace(n) &&
                                string.Equals(btr.Name, n, StringComparison.OrdinalIgnoreCase)))
                            {
                                isPileCandidate = true;
                            }
                            else
                            {
                                // 2.b) Name hints inside block name
                                if (btr.Name.IndexOf("coc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    btr.Name.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    btr.Name.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isPileCandidate = true;
                                }
                                // 2.c) Attribute-based heuristic: blocks that define attributes often represent labeled objects
                                else if (btr.HasAttributeDefinitions)
                                {
                                    isPileCandidate = true;
                                }
                                // 2.d) Geometry heuristic: simple footprint (circle/pline/line) inside block definition
                                else
                                {
                                    foreach (ObjectId childId in btr)
                                    {
                                        DBObject child = tr.GetObject(childId, OpenMode.ForRead);
                                        if (child is Circle || child is Polyline || child is Line)
                                        {
                                            isPileCandidate = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (isPileCandidate)
                            {
                                piles.Add((selObj.ObjectId, br.Position));
                            }
                        }
                    }

                    if (piles.Count == 0)
                    {
                        ed.WriteMessage("\nDTC: No pile blocks found in selection.\n");
                        tr.Commit();
                        return;
                    }

                    // sort using saved ordering (row-first priority)
                    var sorted = SortByOrdering(piles, XLDatTenCocSetting.Ordering);

                    int num = XLDatTenCocSetting.StartNumber;
                    double height = XLDatTenCocSetting.TextHeight;
                    string prefix = XLDatTenCocSetting.Prefix ?? string.Empty;

                    // Build list of existing DBText in selected area for potential update
                    var texts = new List<(ObjectId id, DBText text, Point3d pos)>();
                    foreach (SelectedObject selObj in psr.Value)
                    {
                        DBObject obj = tr.GetObject(selObj.ObjectId, OpenMode.ForRead);
                        if (obj is DBText dt)
                        {
                            texts.Add((selObj.ObjectId, dt, dt.Position));
                        }
                    }

                    // Use a tighter, height-based tolerance for matching nearby text.
                    double tolerance = (height > 0)
                        ? Math.Max(height * 0.6, 2.0)
                        : 100.0;

                    // small threshold under which a text is considered "already at block position"
                    double nearBlockThreshold = (height > 0) ? Math.Max(height * 0.15, POSITION_EPS * 10) : 1.0;

                    // Resolve TextStyleId from setting
                    ObjectId selectedTextStyleId = ObjectId.Null;
                    string textStyleName = XLDatTenCocSetting.TextStyleName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(textStyleName))
                    {
                        try
                        {
                            TextStyleTable tst = tr.GetObject(db.TextStyleTableId, OpenMode.ForRead) as TextStyleTable;
                            if (tst != null)
                            {
                                foreach (ObjectId tsId in tst)
                                {
                                    TextStyleTableRecord tsr = tr.GetObject(tsId, OpenMode.ForRead) as TextStyleTableRecord;
                                    if (tsr != null && string.Equals(tsr.Name, textStyleName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        selectedTextStyleId = tsId;
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            selectedTextStyleId = ObjectId.Null;
                        }
                    }

                    // prepare modelSpace for writes when needed
                    foreach (var p in sorted)
                    {
                        string textValue = string.IsNullOrEmpty(prefix) ? num.ToString() : prefix + num.ToString();

                        // find nearest existing text within tolerance
                        ObjectId foundTextId = ObjectId.Null;
                        double bestDist = double.MaxValue;
                        foreach (var t in texts)
                        {
                            double d = p.pos.DistanceTo(t.pos);
                            if (d < bestDist && d <= tolerance)
                            {
                                bestDist = d;
                                foundTextId = t.id;
                            }
                        }

                        if (!foundTextId.IsNull && bestDist <= nearBlockThreshold)
                        {
                            // update existing text only when it is already very near the block insertion point
                            DBText existing = tr.GetObject(foundTextId, OpenMode.ForWrite) as DBText;
                            if (existing != null)
                            {
                                existing.TextString = textValue;
                                if (height > 0) existing.Height = height;
                                if (!selectedTextStyleId.IsNull)
                                    existing.TextStyleId = selectedTextStyleId;

                                // ensure the text position exactly matches the block insertion point
                                existing.Position = new Point3d(p.pos.X, p.pos.Y, p.pos.Z);
                            }
                        }
                        else
                        {
                            // create new DBText at the block insertion point so text coincides with block position
                            DBText newText = new DBText()
                            {
                                Position = p.pos,
                                TextString = textValue,
                                Height = (height > 0 ? height : 300.0),
                                Rotation = 0
                            };
                            newText.SetDatabaseDefaults(db);

                            if (!selectedTextStyleId.IsNull)
                                newText.TextStyleId = selectedTextStyleId;

                            // append to modelspace (upgrade only when needed)
                            ms.UpgradeOpen();
                            ms.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);

                            // also add to texts list so subsequent checks see it
                            texts.Add((newText.ObjectId, newText, newText.Position));
                        }

                        num++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nDTC: Assigned names to {sorted.Count} piles starting at {XLDatTenCocSetting.StartNumber} (prefix='{prefix}', TextStyle='{textStyleName}').\n");
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DTCAction), ex);
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
            }
        }

        /// <summary>
        /// Sorts piles by ordering mode with ROW (Y-axis) as primary sort key and COLUMN (X-axis) as secondary.
        /// 
        /// Ordering modes:
        /// 0: Top→Bottom (descending Y), Left→Right (ascending X)
        /// 1: Top→Bottom (descending Y), Right→Left (descending X)
        /// 2: Bottom→Top (ascending Y), Left→Right (ascending X)
        /// 3: Bottom→Top (ascending Y), Right→Left (descending X)
        /// </summary>
        private List<(ObjectId id, Point3d pos)> SortByOrdering(List<(ObjectId id, Point3d pos)> piles, int ordering)
        {
            const double eps = POSITION_EPS;

            Comparison<(ObjectId id, Point3d pos)> cmp = (a, b) =>
            {
                double ax = a.pos.X, ay = a.pos.Y;
                double bx = b.pos.X, by = b.pos.Y;

                switch (ordering)
                {
                    case 0: // Trái→Phải, Trên→Dưới => Y desc (top->bottom), X asc
                        {
                            int c = -CompareDouble(ay, by, eps); // primary: Y descending
                            if (c != 0) return c;
                            return CompareDouble(ax, bx, eps);   // secondary: X ascending
                        }
                    case 1: // Trái→Phải, Dưới→Trên => Y asc (bottom->top), X asc
                        {
                            int c = CompareDouble(ay, by, eps);  // primary: Y ascending
                            if (c != 0) return c;
                            return CompareDouble(ax, bx, eps);   // secondary: X ascending
                        }
                    case 2: // Phải→Trái, Trên→Dưới => Y desc, X desc
                        {
                            int c = -CompareDouble(ay, by, eps); // primary: Y descending
                            if (c != 0) return c;
                            return -CompareDouble(ax, bx, eps);  // secondary: X descending
                        }
                    case 3: // Phải→Trái, Dưới→Trên => Y asc, X desc
                        {
                            int c = CompareDouble(ay, by, eps);  // primary: Y ascending
                            if (c != 0) return c;
                            return -CompareDouble(ax, bx, eps);  // secondary: X descending
                        }
                    default:
                        // fallback: row-first top->bottom then left->right
                        {
                            int c = -CompareDouble(ay, by, eps);
                            if (c != 0) return c;
                            return CompareDouble(ax, bx, eps);
                        }
                }
            };

            var arr = piles.ToList();
            arr.Sort(cmp);
            return arr;
        }

        private int CompareDouble(double a, double b, double eps)
        {
            if (Math.Abs(a - b) <= eps) return 0;
            return a.CompareTo(b);
        }
    }
}