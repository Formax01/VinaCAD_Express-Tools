using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Definitions;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class BlockHelper
    {

        public static string GetBlockName(Database db, ObjectId blockId)
        {
            if (db == null || blockId.IsNull || !blockId.IsValid)
                return string.Empty;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference blockRef =
                    tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;

                if (blockRef == null)
                    return string.Empty;

                string blockName = blockRef.Name;

                if (blockRef.IsDynamicBlock)
                {
                    BlockTableRecord dynamicBtr =
                        tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead)
                        as BlockTableRecord;

                    if (dynamicBtr != null)
                        blockName = dynamicBtr.Name;
                }

                tr.Commit();

                return blockName;
            }
        }

        public static string GetBlockName(Transaction tr, ObjectId blockId)
        {
            if (tr == null || blockId.IsNull || !blockId.IsValid)
                return string.Empty;

            BlockReference blockRef =
                tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;

            if (blockRef == null)
                return string.Empty;

            string blockName = blockRef.Name;

            if (blockRef.IsDynamicBlock)
            {
                BlockTableRecord dynamicBtr =
                    tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead)
                    as BlockTableRecord;

                if (dynamicBtr != null)
                    blockName = dynamicBtr.Name;
            }

            return blockName;
        }

        public static ObjectId? PickBlock(Editor ed)
        {
            if (ed == null)
                return null;

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nChọn block: ";
            opt.SingleOnly = true;
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK ||
                result.Value == null ||
                result.Value.Count == 0)
            {
                return null;
            }

            return result.Value[0].ObjectId;
        }

        public static List<ObjectId> PickBlocksByBlockName(Database db, Editor ed, string name)
        {
            List<ObjectId> resultIds = new List<ObjectId>();

            if (db == null || ed == null)
                return resultIds;

            if (string.IsNullOrWhiteSpace(name))
                return resultIds;

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn block: ";
            opt.RejectObjectsOnLockedLayers = true;

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "INSERT"),
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return resultIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    BlockReference blockRef =
                        tr.GetObject(selectedObj.ObjectId, OpenMode.ForRead) as BlockReference;

                    if (blockRef == null)
                        continue;

                    string blockName = GetBlockName(tr, selectedObj.ObjectId);

                    if (string.Equals(blockName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        resultIds.Add(selectedObj.ObjectId);
                    }
                }

                tr.Commit();
            }
          
            return resultIds;
        }

        public static List<ObjectId> PickBlocksByBlockNameContains(Database db, Editor ed, string name)
        {
            List<ObjectId> resultIds = new List<ObjectId>();

            if (db == null || ed == null)
                return resultIds;

            if (string.IsNullOrWhiteSpace(name))
                return resultIds;

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn block: ";

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return resultIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    BlockReference blockRef =
                        tr.GetObject(selectedObj.ObjectId, OpenMode.ForRead) as BlockReference;

                    if (blockRef == null)
                        continue;

                    string blockName = GetBlockName(tr, selectedObj.ObjectId);

                    if (!string.IsNullOrEmpty(blockName) &&
                        blockName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resultIds.Add(selectedObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            if (resultIds.Any())
            {
                TextHelper.ClearSelection(ed);
                ed.SetImpliedSelection(resultIds.ToArray());
                ed.UpdateScreen();
            }

            return resultIds;
        }

        public static List<ObjectId> PickBlocksByBlockNameContains(Database db, Editor ed, List<string> names)
        {
            List<ObjectId> resultIds = new List<ObjectId>();

            if (db == null || ed == null)
                return resultIds;

            if (names == null || names.Count == 0)
                return resultIds;

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn block: ";

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return resultIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    BlockReference blockRef =
                        tr.GetObject(selectedObj.ObjectId, OpenMode.ForRead) as BlockReference;

                    if (blockRef == null)
                        continue;

                    string blockName = GetBlockName(tr, selectedObj.ObjectId);

                    if (string.IsNullOrWhiteSpace(blockName))
                        continue;

                    bool matched = names.Any(x =>
                        !string.IsNullOrWhiteSpace(x) &&
                        blockName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (matched)
                    {
                        resultIds.Add(selectedObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            if (resultIds.Any())
            {
                TextHelper.ClearSelection(ed);
                ed.SetImpliedSelection(resultIds.ToArray());
                ed.UpdateScreen();
            }

            return resultIds;
        }

        public static void PickBlocksByNamesAndTexts(
            Database db,
            Editor ed,
            List<string> blockNames,
            out List<ObjectId> blockIds,
            out List<ObjectId> textIds)
        {
            blockIds = new List<ObjectId>();
            textIds = new List<ObjectId>();

            if (db == null || ed == null)
                return;

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn block và text: ";

            PromptSelectionResult result = ed.GetSelection(opt);

            if (result.Status != PromptStatus.OK)
                return;

            HashSet<string> blockNameSet = blockNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .ToHashSet();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    Entity ent =
                        tr.GetObject(selectedObj.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent is DBText || ent is MText)
                    {
                        textIds.Add(selectedObj.ObjectId);
                        continue;
                    }

                    if (ent is BlockReference)
                    {
                        string blockName = GetBlockName(tr, selectedObj.ObjectId);

                        if (!string.IsNullOrWhiteSpace(blockName) &&
                            blockNameSet.Contains(blockName.ToUpper()))
                        {
                            blockIds.Add(selectedObj.ObjectId);
                        }
                    }
                }

                tr.Commit();
            }

            List<ObjectId> allIds = new List<ObjectId>();
            allIds.AddRange(blockIds);
            allIds.AddRange(textIds);

            if (allIds.Any())
            {
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.SetImpliedSelection(allIds.ToArray());
            }

            ed.UpdateScreen();
        }

        public static List<ObjectId> ScanTextIdsContains(Database db, Editor ed, string findText)
        {
            List<ObjectId> textIds = new List<ObjectId>();

            if (db == null || ed == null)
                return textIds;

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn vùng chứa text: ";
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return textIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in result.Value)
                {
                    if (selObj == null)
                        continue;

                    Entity ent =
                        tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent is DBText dbText)
                    {
                        if (!string.IsNullOrEmpty(dbText.TextString) &&
                            dbText.TextString.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            textIds.Add(selObj.ObjectId);
                        }
                    }
                    else if (ent is MText mText)
                    {
                        if (!string.IsNullOrEmpty(mText.Contents) &&
                            mText.Contents.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            textIds.Add(selObj.ObjectId);
                        }
                    }
                }

                tr.Commit();
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            return textIds;
        }

        public static List<ObjectId> SelectTextIds(Database db, Editor ed)
        {
            List<ObjectId> textIds = new List<ObjectId>();

            if (db == null || ed == null)
                return textIds;

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn vùng chứa text: ";
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return textIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in result.Value)
                {
                    if (selObj == null)
                        continue;

                    Entity ent =
                        tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent is DBText || ent is MText)
                    {
                        textIds.Add(selObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            return textIds;
        }

        public static List<ObjectId> SortBlocksTopToBottomLeftToRight(
            Database db,
            List<ObjectId> blockIds,
            double tolerance)
        {
            List<BlockSortInfo> blockInfos = GetBlockSortInfos(db, blockIds);

            List<List<BlockSortInfo>> columns = new List<List<BlockSortInfo>>();

            foreach (BlockSortInfo block in blockInfos.OrderBy(x => x.X))
            {
                List<BlockSortInfo> column = columns
                    .FirstOrDefault(c => Math.Abs(c.Average(x => x.X) - block.X) <= tolerance);

                if (column == null)
                    columns.Add(new List<BlockSortInfo> { block });
                else
                    column.Add(block);
            }

            return columns
                .OrderBy(c => c.Average(x => x.X))
                .SelectMany(c => c.OrderByDescending(x => x.Y))
                .Select(x => x.ObjectId)
                .ToList();
        }

        public static List<ObjectId> SortBlocksLeftToRightTopToBottom(
            Database db,
            List<ObjectId> blockIds,
            double tolerance)
        {
            List<BlockSortInfo> blockInfos = GetBlockSortInfos(db, blockIds);

            List<List<BlockSortInfo>> rows = new List<List<BlockSortInfo>>();

            foreach (BlockSortInfo block in blockInfos.OrderByDescending(x => x.Y))
            {
                List<BlockSortInfo> row = rows
                    .FirstOrDefault(r => Math.Abs(r.Average(x => x.Y) - block.Y) <= tolerance);

                if (row == null)
                    rows.Add(new List<BlockSortInfo> { block });
                else
                    row.Add(block);
            }

            return rows
                .OrderByDescending(r => r.Average(x => x.Y))
                .SelectMany(r => r.OrderBy(x => x.X))
                .Select(x => x.ObjectId)
                .ToList();
        }

        public static List<BlockSortInfo> GetBlockSortInfos(Database db, List<ObjectId> blockIds)
        {
            List<BlockSortInfo> blockInfos = new List<BlockSortInfo>();

            if (db == null || blockIds == null || blockIds.Count == 0)
                return blockInfos;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    if (id.IsNull || !id.IsValid)
                        continue;

                    BlockReference br =
                        tr.GetObject(id, OpenMode.ForRead) as BlockReference;

                    if (br == null)
                        continue;

                    blockInfos.Add(new BlockSortInfo
                    {
                        ObjectId = id,
                        X = br.Position.X,
                        Y = br.Position.Y
                    });
                }

                tr.Commit();
            }

            return blockInfos;
        }

        public static Dictionary<string, AttributeReference> GetAttributeReferences(
            BlockReference blockRef,
            Transaction tr)
        {
            Dictionary<string, AttributeReference> result =
                new Dictionary<string, AttributeReference>(StringComparer.OrdinalIgnoreCase);

            if (blockRef == null || tr == null)
                return result;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef =
                    tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                result[attRef.Tag] = attRef;
            }

            return result;
        }

        public static void SetAttr(Dictionary<string, AttributeReference> atts, string tag, string value)
        {
            if (atts == null || string.IsNullOrWhiteSpace(tag))
                return;

            if (atts.TryGetValue(tag, out AttributeReference attRef))
            {
                attRef.TextString = value ?? "";
            }
        }

        public static Dictionary<string, string> GetBlockAttributes(
            BlockReference blockRef,
            Transaction tr)
        {
            Dictionary<string, string> attrs =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (blockRef == null || tr == null)
                return attrs;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef =
                    tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                if (attRef == null)
                    continue;

                attrs[attRef.Tag] = attRef.TextString;
            }

            return attrs;
        }

        public static void SetDrawingNoAttributes(
            Database db,
            List<ObjectId> blockIds,
            string nameDrawingNo,
            string prefix,
            string suffix)
        {
            if (db == null || blockIds == null || blockIds.Count == 0)
                return;

            int startNumber = 0;
            int numberWidth = 0;

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                numberWidth = suffix.Length;
                int.TryParse(suffix, out startNumber);
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < blockIds.Count; i++)
                {
                    BlockReference br =
                        tr.GetObject(blockIds[i], OpenMode.ForWrite) as BlockReference;

                    if (br == null)
                        continue;

                    int currentNumber = startNumber + i;

                    string numberText;

                    if (numberWidth > 0)
                        numberText = currentNumber.ToString($"D{numberWidth}");
                    else
                        numberText = currentNumber.ToString();

                    string newValue = $"{prefix}{numberText}";

                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        AttributeReference attRef =
                            tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                        if (attRef == null)
                            continue;

                        if (attRef.Tag.Equals(nameDrawingNo, StringComparison.OrdinalIgnoreCase))
                        {
                            attRef.TextString = newValue;
                            break;
                        }
                    }
                }

                tr.Commit();
            }
        }

        public static void SetDrawingNoAttributesByAlphabet(
            Database db,
            List<ObjectId> blockIds,
            string startingTag)
        {
            if (db == null || blockIds == null || blockIds.Count == 0)
                return;

            string prefix = string.Empty;
            string startAlpha = "A";

            Match match = Regex.Match(startingTag ?? "", @"([A-Za-z]+)$");

            if (match.Success)
            {
                startAlpha = match.Value.ToUpper();

                prefix = startingTag.Substring(
                    0,
                    startingTag.Length - match.Value.Length);
            }
            else
            {
                prefix = startingTag ?? "";
            }

            int startIndex = AlphabetToNumber(startAlpha);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < blockIds.Count; i++)
                {
                    BlockReference br =
                        tr.GetObject(blockIds[i], OpenMode.ForWrite) as BlockReference;

                    if (br == null)
                        continue;

                    string alpha = NumberToAlphabet(startIndex + i);

                    string newValue = $"{prefix}{alpha}";

                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        AttributeReference attRef =
                            tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                        if (attRef == null)
                            continue;

                        if (attRef.Tag.Equals("DRAWINGNO", StringComparison.OrdinalIgnoreCase))
                        {
                            attRef.TextString = newValue;
                            break;
                        }
                    }
                }

                tr.Commit();
            }
        }

        public static Dictionary<string, string> GetAttributes(Database db, ObjectId blockId)
        {
            Dictionary<string, string> attributes = new Dictionary<string, string>();

            if (db == null || blockId.IsNull || !blockId.IsValid)
                return attributes;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference blockRef =
                    tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;

                if (blockRef == null)
                    return attributes;

                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    AttributeReference attRef =
                        tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                    if (attRef == null)
                        continue;

                    string tag = attRef.Tag?.ToUpper();

                    if (string.IsNullOrWhiteSpace(tag))
                        continue;

                    attributes[tag] = attRef.TextString;
                }

                tr.Commit();
            }

            return attributes;
        }

        public static List<DrawingCategoriesModel> GetDrawingCategories(
            Database db,
            List<ObjectId> blockIds,
            object drawingNameAttribute1,
            object drawingNameAttribute2,
            object drawingNameAttribute3,
            string drawingNoAttribute)
        {
            List<DrawingCategoriesModel> results = new List<DrawingCategoriesModel>();

            if (db == null || blockIds == null || blockIds.Count == 0)
                return results;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < blockIds.Count; i++)
                {
                    BlockReference br =
                        tr.GetObject(blockIds[i], OpenMode.ForRead) as BlockReference;

                    if (br == null)
                        continue;

                    string drawingName1 = string.Empty;
                    string drawingName2 = string.Empty;
                    string drawingName3 = string.Empty;
                    string drawingNo = string.Empty;

                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        AttributeReference attRef =
                            tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                        if (attRef == null)
                            continue;

                        if (drawingNameAttribute1 != null &&
                            attRef.Tag.Equals(drawingNameAttribute1.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            drawingName1 = attRef.TextString;
                        }

                        if (drawingNameAttribute2 != null &&
                            attRef.Tag.Equals(drawingNameAttribute2.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            drawingName2 = attRef.TextString;
                        }

                        if (drawingNameAttribute3 != null &&
                            attRef.Tag.Equals(drawingNameAttribute3.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            drawingName3 = attRef.TextString;
                        }

                        if (!string.IsNullOrWhiteSpace(drawingNoAttribute) &&
                            attRef.Tag.Equals(drawingNoAttribute, StringComparison.OrdinalIgnoreCase))
                        {
                            drawingNo = attRef.TextString;
                        }
                    }

                    results.Add(new DrawingCategoriesModel
                    {
                        STT = (i + 1).ToString(),
                        DrawingName = drawingName1 + " " + drawingName2 + " " + drawingName3,
                        DrawingNo = drawingNo
                    });
                }

                tr.Commit();
            }

            return results;
        }

        public static int CloneBlocksWithNewName(
            Database db,
            List<ObjectId> oldBlockIds,
            string newBlockName)
        {
            int createdCount = 0;

            if (db == null)
                return createdCount;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                    return createdCount;

                if (blockTable.Has(newBlockName))
                {
                    MessageBox.Show(
                        $"Block \"{newBlockName}\" đã tồn tại trong bản vẽ.",
                        StringDefinition.TITLE_MESSAGE);

                    return createdCount;
                }

                if (oldBlockIds == null || oldBlockIds.Count == 0)
                    return createdCount;

                BlockReference firstOldBlockRef =
                    tr.GetObject(oldBlockIds[0], OpenMode.ForRead) as BlockReference;

                if (firstOldBlockRef == null)
                    return createdCount;

                BlockTableRecord oldBlockDef =
                    tr.GetObject(firstOldBlockRef.BlockTableRecord, OpenMode.ForRead)
                    as BlockTableRecord;

                if (oldBlockDef == null)
                    return createdCount;

                blockTable.UpgradeOpen();

                BlockTableRecord newBlockDef = new BlockTableRecord
                {
                    Name = newBlockName,
                    Origin = oldBlockDef.Origin
                };

                ObjectId newBlockDefId = blockTable.Add(newBlockDef);
                tr.AddNewlyCreatedDBObject(newBlockDef, true);

                foreach (ObjectId entId in oldBlockDef)
                {
                    Entity oldEnt =
                        tr.GetObject(entId, OpenMode.ForRead) as Entity;

                    if (oldEnt == null)
                        continue;

                    Entity newEnt = oldEnt.Clone() as Entity;

                    if (newEnt == null)
                        continue;

                    newBlockDef.AppendEntity(newEnt);
                    tr.AddNewlyCreatedDBObject(newEnt, true);
                }

                BlockTableRecord modelSpace =
                    tr.GetObject(
                        blockTable[BlockTableRecord.ModelSpace],
                        OpenMode.ForWrite) as BlockTableRecord;

                if (modelSpace == null)
                    return createdCount;

                foreach (ObjectId oldBlockId in oldBlockIds)
                {
                    BlockReference oldBlockRef =
                        tr.GetObject(oldBlockId, OpenMode.ForRead) as BlockReference;

                    if (oldBlockRef == null)
                        continue;

                    BlockReference newBlockRef =
                        new BlockReference(oldBlockRef.Position, newBlockDefId);

                    newBlockRef.SetDatabaseDefaults(db);

                    modelSpace.AppendEntity(newBlockRef);
                    tr.AddNewlyCreatedDBObject(newBlockRef, true);

                    newBlockRef.Rotation = oldBlockRef.Rotation;
                    newBlockRef.ScaleFactors = oldBlockRef.ScaleFactors;
                    newBlockRef.Normal = oldBlockRef.Normal;

                    if (!oldBlockRef.LayerId.IsNull && oldBlockRef.LayerId.IsValid)
                    {
                        newBlockRef.LayerId = oldBlockRef.LayerId;
                    }

                    if (!oldBlockRef.LinetypeId.IsNull && oldBlockRef.LinetypeId.IsValid)
                    {
                        newBlockRef.LinetypeId = oldBlockRef.LinetypeId;
                    }

                    newBlockRef.Color = oldBlockRef.Color;
                    newBlockRef.LineWeight = oldBlockRef.LineWeight;

                    CopyAttributes(tr, oldBlockRef, newBlockRef, newBlockDef);

                    oldBlockRef.UpgradeOpen();
                    oldBlockRef.Erase();

                    createdCount++;
                }

                tr.Commit();
            }

            return createdCount;
        }

        private static void CopyAttributes(
            Transaction tr,
            BlockReference oldBlockRef,
            BlockReference newBlockRef,
            BlockTableRecord newBlockDef)
        {
            foreach (ObjectId id in newBlockDef)
            {
                AttributeDefinition attDef =
                    tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;

                if (attDef == null || attDef.Constant)
                    continue;

                AttributeReference attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, newBlockRef.BlockTransform);

                AttributeReference oldAttRef = null;

                foreach (ObjectId oldAttId in oldBlockRef.AttributeCollection)
                {
                    AttributeReference tempOldAtt =
                        tr.GetObject(oldAttId, OpenMode.ForRead) as AttributeReference;

                    if (tempOldAtt != null && tempOldAtt.Tag == attDef.Tag)
                    {
                        oldAttRef = tempOldAtt;
                        break;
                    }
                }

                if (oldAttRef != null)
                {
                    attRef.TextString = oldAttRef.TextString;
                    attRef.Position = oldAttRef.Position;
                    attRef.Rotation = oldAttRef.Rotation;
                    attRef.Height = oldAttRef.Height;
                }

                newBlockRef.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }

        public static bool CheckTableBlocksExist(BlockTable bt, List<string> blockNames)
        {
            if (bt == null || blockNames == null || blockNames.Count == 0)
                return false;

            bool isValid = blockNames.All(x => bt.Has(x));

            if (isValid)
                return true;

            MessageBox.Show(
                "Bạn cần chèn block thống kê mẫu vào bản vẽ.",
                StringDefinition.TITLE_MESSAGE);

            return false;
        }

        public static bool CheckTableBlocksExist(BlockTable bt, string[] blockNames)
        {
            if (blockNames == null)
                return false;

            return CheckTableBlocksExist(bt, blockNames.ToList());
        }

        private static int AlphabetToNumber(string alpha)
        {
            int result = 0;

            foreach (char c in alpha)
            {
                result *= 26;
                result += c - 'A' + 1;
            }

            return result - 1;
        }

        private static string NumberToAlphabet(int number)
        {
            number++;

            string result = string.Empty;

            while (number > 0)
            {
                number--;

                result = (char)('A' + number % 26) + result;

                number /= 26;
            }

            return result;
        }

        public class BlockSortInfo
        {
            public ObjectId ObjectId { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }
    }

}