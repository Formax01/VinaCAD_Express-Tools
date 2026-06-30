using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;

namespace Tools.VinaCad.Helper.Helper
{
    public class BlockTemplateLoader
    {
        public static string GetTemplatePath(string templateFileName = "TemplateBlocks.dwg")
        {
            string dllFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            DirectoryInfo dir = new DirectoryInfo(dllFolder);

            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Resources")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
                throw new DirectoryNotFoundException("Không tìm thấy thư mục Resources.");

            return Path.Combine(dir.FullName, "Resources", "Templates", templateFileName);
        }

        public static void LoadAllBlocks(Database currentDb,string templatePath,DuplicateRecordCloning duplicateMode = DuplicateRecordCloning.Ignore)
        {
            LoadBlocks(currentDb, templatePath, null, duplicateMode);
        }

        public static void LoadBlocksByNames(Database currentDb,string templatePath,IEnumerable<string> blockNames,DuplicateRecordCloning duplicateMode = DuplicateRecordCloning.Ignore)
        {
            if (blockNames == null)
                return;

            HashSet<string> names = new HashSet<string>(
                blockNames.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            if (names.Count == 0)
                return;

            LoadBlocks(currentDb, templatePath, names, duplicateMode);
        }

        public static bool LoadBlocksFromFile(  Database currentDb, string dwgFilePath, List<string> blockNames, DuplicateRecordCloning duplicateMode = DuplicateRecordCloning.Ignore)
        {
            if (currentDb == null)
                return false;

            if (!File.Exists(dwgFilePath) || blockNames == null || blockNames.Count == 0)
                return false;

            using (Database sourceDb = new Database(false, true))
            {
                try
                {
                    sourceDb.ReadDwgFile(dwgFilePath, FileShare.Read, true, null);

                    ObjectIdCollection idsToImport = new ObjectIdCollection();

                    using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable bt =
                            tr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead) as BlockTable;

                        if (bt == null)
                            return false;

                        foreach (string name in blockNames)
                        {
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            if (bt.Has(name))
                            {
                                idsToImport.Add(bt[name]);
                            }
                        }

                        tr.Commit();
                    }

                    if (idsToImport.Count == 0)
                        return false;

                    IdMapping mapping = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        idsToImport,
                        currentDb.BlockTableId,
                        mapping,
                        duplicateMode,
                        false);

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool LoadDimStylesFromFile(Database currentDb, string dwgFilePath,  List<string> dimStyleNames, DuplicateRecordCloning duplicateMode = DuplicateRecordCloning.Replace)
        {
            if (currentDb == null)
                return false;

            if (!File.Exists(dwgFilePath) || dimStyleNames == null || dimStyleNames.Count == 0)
                return false;

            using (Database sourceDb = new Database(false, true))
            {
                try
                {
                    sourceDb.ReadDwgFile(dwgFilePath, FileShare.Read, true, null);

                    ObjectIdCollection idsToImport = new ObjectIdCollection();

                    using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
                    {
                        DimStyleTable dimStyleTable =
                            tr.GetObject(sourceDb.DimStyleTableId, OpenMode.ForRead) as DimStyleTable;

                        if (dimStyleTable == null)
                            return false;

                        foreach (string name in dimStyleNames)
                        {
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            if (dimStyleTable.Has(name))
                            {
                                idsToImport.Add(dimStyleTable[name]);
                            }
                        }

                        tr.Commit();
                    }

                    if (idsToImport.Count == 0)
                        return false;

                    IdMapping mapping = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        idsToImport,
                        currentDb.DimStyleTableId,
                        mapping,
                        duplicateMode,
                        false);

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void LoadBlocks(  Database currentDb, string templatePath, HashSet<string> blockNames,  DuplicateRecordCloning duplicateMode)
        {
            if (currentDb == null)
                throw new ArgumentNullException(nameof(currentDb));

            if (string.IsNullOrWhiteSpace(templatePath))
                throw new ArgumentException("Đường dẫn file template không hợp lệ.", nameof(templatePath));

            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Không tìm thấy file template block.", templatePath);

            using (Database sourceDb = new Database(false, true))
            {
                try
                {
                    sourceDb.ReadDwgFile(templatePath, FileShare.Read, true, null);

                    ObjectIdCollection idsToClone = new ObjectIdCollection();

                    using (Transaction sourceTr = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable sourceBt =
                            sourceTr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead) as BlockTable;

                        if (sourceBt == null)
                            return;

                        foreach (ObjectId blockId in sourceBt)
                        {
                            BlockTableRecord sourceBtr =
                                sourceTr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;

                            if (!IsValidTemplateBlock(sourceBtr))
                                continue;

                            if (blockNames != null && !blockNames.Contains(sourceBtr.Name))
                                continue;

                            idsToClone.Add(blockId);
                        }

                        sourceTr.Commit();
                    }

                    if (idsToClone.Count == 0)
                        return;

                    IdMapping mapping = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        idsToClone,
                        currentDb.BlockTableId,
                        mapping,
                        duplicateMode,
                        false);
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message, ex);
                }
            }
        }
        public static bool LoadAllObjectsFromFile(
    Database currentDb,
    Editor ed,
    string dwgFilePath,
    DuplicateRecordCloning duplicateMode = DuplicateRecordCloning.Ignore)
        {
            if (currentDb == null || ed == null)
                return false;

            if (string.IsNullOrWhiteSpace(dwgFilePath) || !File.Exists(dwgFilePath))
                return false;

            try
            {
                PromptPointResult ppr = ed.GetPoint(
                    "\nChọn điểm đặt đối tượng từ template: ");

                if (ppr.Status != PromptStatus.OK)
                    return false;

                Point3d insertPoint = ppr.Value;

                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(dwgFilePath, FileShare.Read, true, null);

                    string tempBlockName =
                        "__TEMPLATE_IMPORT_" + Guid.NewGuid().ToString("N");

                    ObjectId tempBlockDefId =
                        currentDb.Insert(tempBlockName, sourceDb, true);

                    if (tempBlockDefId.IsNull)
                        return false;

                    using (Transaction tr = currentDb.TransactionManager.StartTransaction())
                    {
                        BlockTable bt =
                            tr.GetObject(currentDb.BlockTableId, OpenMode.ForRead) as BlockTable;

                        if (bt == null)
                            return false;

                        BlockTableRecord modelSpace =
                            tr.GetObject(
                                bt[BlockTableRecord.ModelSpace],
                                OpenMode.ForWrite) as BlockTableRecord;

                        if (modelSpace == null)
                            return false;

                        BlockReference tempBlockRef =
                            new BlockReference(insertPoint, tempBlockDefId);

                        modelSpace.AppendEntity(tempBlockRef);
                        tr.AddNewlyCreatedDBObject(tempBlockRef, true);

                        tempBlockRef.ExplodeToOwnerSpace();

                        tempBlockRef.Erase();

                        tr.Commit();
                    }
                }

                ed.Regen();
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static bool CheckTableBlocksExist(BlockTable bt, string[] blockNames)
        {
            if (bt == null || blockNames == null || blockNames.Length == 0)
                return false;

            foreach (string blockName in blockNames)
            {
                if (string.IsNullOrWhiteSpace(blockName))
                    continue;

                if (!bt.Has(blockName))
                {
                    MessageBox.Show(
                        $"Không tìm thấy block '{blockName}' trong bản vẽ.",
                        StringDefinition.TITLE_MESSAGE);

                    return false;
                }
            }

            return true;
        }

        public static bool CheckTableBlocksExist(BlockTable bt, List<string> blockNames)
        {
            if (blockNames == null)
                return false;

            return CheckTableBlocksExist(bt, blockNames.ToArray());
        }

        private static bool IsValidTemplateBlock(BlockTableRecord block)
        {
            if (block == null)
                return false;

            if (block.IsLayout)
                return false;

            if (block.IsAnonymous)
                return false;

            if (string.IsNullOrWhiteSpace(block.Name))
                return false;

            if (block.Name.StartsWith("*"))
                return false;

            return true;
        }
    }
}
