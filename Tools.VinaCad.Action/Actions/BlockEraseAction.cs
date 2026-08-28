using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class BlockEraseAction
    {
        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                int erasedCount;

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> dynamicDefinitionIds = new HashSet<ObjectId>();
                    erasedCount = EraseInteractively(editor,transaction,dynamicDefinitionIds);

                    if (erasedCount > 0)
                    {
                        BlockEraseHelper.UpdateDynamicDefinitions(transaction, dynamicDefinitionIds);
                        transaction.Commit();
                    }
                }

                editor.Regen();

                if (erasedCount == 0)
                {
                    editor.WriteMessage("\nBBE: Không có đối tượng nào được xóa.");
                    return;
                }

                editor.WriteMessage($"\nBBE: Đã xóa {erasedCount} đối tượng khỏi block. " +"Các block reference dùng chung definition đã được cập nhật.");
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BlockEraseAction), ex);
                editor.Regen();
                editor.WriteMessage($"\nLỗi BBE: {ex.Message}");
            }
        }

        private static int EraseInteractively(Editor editor,Transaction transaction,HashSet<ObjectId> dynamicDefinitionIds)
        {
            int erasedCount = 0;

            editor.WriteMessage("\nBBE: Chọn các đối tượng bên trong block cần xóa; " +"nhấn Enter, Space hoặc gõ Xong để kết thúc.");

            while (true)
            {
                PromptNestedEntityOptions options = new PromptNestedEntityOptions($"\nChọn đối tượng trong block [{erasedCount} đã chọn]: ");
                options.AllowNone = true;
                options.Keywords.Add("Xong");
                options.AppendKeywordsToMessage = true;

                PromptNestedEntityResult result = editor.GetNestedEntity(options);

                if (result.Status == PromptStatus.Keyword &&
                    string.Equals(result.StringResult, "Xong", StringComparison.OrdinalIgnoreCase))
                    return erasedCount;

                if (result.Status == PromptStatus.None)
                    return erasedCount;

                if (result.Status == PromptStatus.Cancel)
                    return erasedCount;

                if (result.Status != PromptStatus.OK)
                {
                    editor.WriteMessage("\nBBE: Đã dừng nhận lựa chọn và lưu các đối tượng đã xóa.");
                    return erasedCount;
                }

                BlockEraseHelper.EraseResult eraseResult = BlockEraseHelper.TryEraseSingleEntity(transaction,result.ObjectId,result.GetContainers());

                if (!eraseResult.Succeeded)
                {
                    editor.WriteMessage($"\nBBE: {eraseResult.RejectionReason}");
                    continue;
                }

                if (!eraseResult.DynamicDefinitionId.IsNull) dynamicDefinitionIds.Add(eraseResult.DynamicDefinitionId);

                erasedCount++;

                // Làm mới toàn bộ hình ảnh sau mỗi lần xóa để đối tượng biến mất
                // ngay trên mọi block reference. Sau đó lệnh tiếp tục chờ lần click kế tiếp.
                editor.Regen();
            }
        }
    }
}
