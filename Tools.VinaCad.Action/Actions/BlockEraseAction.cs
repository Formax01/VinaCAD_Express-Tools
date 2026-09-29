using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Teigha.DatabaseServices;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class BlockEraseAction
    {
        private const int VirtualKeyEscape = 0x1B;
        private const int VirtualKeySpace = 0x20;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                InteractiveEraseResult result;
                bool committed = false;

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> dynamicDefinitionIds = new HashSet<ObjectId>();
                    result = EraseInteractively(editor,transaction,dynamicDefinitionIds);

                    if (!result.Cancelled && result.ErasedCount > 0)
                    {
                        BlockEraseHelper.UpdateDynamicDefinitions(transaction, dynamicDefinitionIds);
                        transaction.Commit();
                        committed = true;
                    }
                }

                editor.Regen();

                if (result.Cancelled)
                    editor.WriteMessage("\nBBE: Đã hủy, bản vẽ không thay đổi.");
                else if (committed)
                    editor.WriteMessage($"\nBBE: Đã xóa {result.ErasedCount} đối tượng.");
                else
                    editor.WriteMessage("\nBBE: Chưa chọn đối tượng nào.");
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BlockEraseAction), ex);
                editor.Regen();
                editor.WriteMessage($"\nLỗi BBE: {ex.Message}");
            }
        }

        private readonly struct InteractiveEraseResult
        {
            public int ErasedCount { get; init; }
            public bool Cancelled { get; init; }
        }

        private static InteractiveEraseResult EraseInteractively(Editor editor,Transaction transaction,HashSet<ObjectId> dynamicDefinitionIds)
        {
            int erasedCount = 0;
            Dictionary<string, object> originalSystemVariables = new Dictionary<string, object>();

            editor.WriteMessage("\nBBE: Chọn đối tượng con cần xóa | Enter/Space: Xóa | Esc: Hủy/Hoàn tác");

            TrySetSystemVariable(originalSystemVariables, "CMDECHO", (short)0);
            TrySetSystemVariable(originalSystemVariables, "INPUTHISTORYMODE", (short)0);
            TrySetSystemVariable(originalSystemVariables, "CLIPROMPTUPDATE", (short)0);

            try
            {
                while (true)
                {
                    string prompt = $"\nChọn đối tượng";

                    PromptNestedEntityOptions options = new PromptNestedEntityOptions(prompt)
                    {
                        AllowNone = true,
                        AppendKeywordsToMessage = false
                    };

                    PromptNestedEntityResult result = editor.GetNestedEntity(options);

                    if (result.Status == PromptStatus.Cancel)
                    {
                        return new InteractiveEraseResult
                        {
                            ErasedCount = erasedCount,
                            Cancelled = WasEscapePressed()
                        };
                    }

                    if (result.Status == PromptStatus.None)
                    {
                        return new InteractiveEraseResult
                        {
                            ErasedCount = erasedCount,
                            Cancelled = false
                        };
                    }

                    bool hasObject = !result.ObjectId.IsNull && result.ObjectId.IsValid;

                    if (result.Status == PromptStatus.Error && !hasObject)
                    {
                        if (WasSpacePressed())
                        {
                            return new InteractiveEraseResult
                            {
                                ErasedCount = erasedCount,
                                Cancelled = false
                            };
                        }

                        editor.WriteMessage("\nBBE: Không có đối tượng. Hãy chọn lại.");
                        continue;
                    }

                    if (result.Status != PromptStatus.OK && result.Status != PromptStatus.Error)
                    {
                        return new InteractiveEraseResult
                        {
                            ErasedCount = erasedCount,
                            Cancelled = WasEscapePressed()
                        };
                    }

                    if (!hasObject)
                    {
                        if (WasSpacePressed())
                        {
                            return new InteractiveEraseResult
                            {
                                ErasedCount = erasedCount,
                                Cancelled = false
                            };
                        }

                        editor.WriteMessage("\nBBE: Không có đối tượng. Hãy chọn lại.");
                        continue;
                    }

                    BlockEraseHelper.EraseResult eraseResult = BlockEraseHelper.TryEraseSingleEntity(transaction,result.ObjectId,result.GetContainers());
                    if (!eraseResult.Succeeded)
                    {
                        Logger.Info($"{nameof(BlockEraseAction)}.RejectedSelection",new InvalidOperationException(eraseResult.RejectionReason ?? "Không xác định được lý do từ chối."));
                        editor.WriteMessage("\nBBE: Đối tượng không nằm trong block có thể sửa. Hãy chọn lại.");
                        continue;
                    }

                    if (!eraseResult.DynamicDefinitionId.IsNull)
                        dynamicDefinitionIds.Add(eraseResult.DynamicDefinitionId);

                    erasedCount++;
                    editor.Regen();
                }
            }
            finally
            {
                RestoreSystemVariables(originalSystemVariables);
            }
        }

        private static bool WasEscapePressed()
        {
            try
            {
                return (GetAsyncKeyState(VirtualKeyEscape) & 0x8000) != 0;
            }
            catch (Exception ex)
            {
                Logger.Info($"{nameof(BlockEraseAction)}.GetEscapeState", ex);
                return false;
            }
        }

        private static bool WasSpacePressed()
        {
            try
            {
                return (GetAsyncKeyState(VirtualKeySpace) & 0x8000) != 0;
            }
            catch (Exception ex)
            {
                Logger.Info($"{nameof(BlockEraseAction)}.GetSpaceState", ex);
                return false;
            }
        }

        private static void TrySetSystemVariable(Dictionary<string, object> originalValues,string name,object temporaryValue)
        {
            try
            {
                object originalValue = Application.GetSystemVariable(name);
                Application.SetSystemVariable(name, temporaryValue);
                originalValues[name] = originalValue;
            }
            catch (Exception ex)
            {
                Logger.Info($"{nameof(BlockEraseAction)}.{name}", ex);
            }
        }

        private static void RestoreSystemVariables(Dictionary<string, object> originalValues)
        {
            foreach (KeyValuePair<string, object> item in originalValues)
            {
                try
                {
                    Application.SetSystemVariable(item.Key, item.Value);
                }
                catch (Exception ex)
                {
                    Logger.Info($"{nameof(BlockEraseAction)}.Restore{item.Key}", ex);
                }
            }
        }
    }
}
