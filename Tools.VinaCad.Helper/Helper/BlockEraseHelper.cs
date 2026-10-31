using Teigha.DatabaseServices;
using System.Collections.Generic;
using Tools.Resources.Language;

namespace Tools.VinaCad.Helper.Helper
{
    public static class BlockEraseHelper
    {
        /// Kiểm tra đối tượng vừa chọn có thể xóa khỏi định nghĩa block hay không.
        public static string? ValidateSelection(Transaction transaction,ObjectId entityId,ObjectId[] containerIds)
        {
            if (entityId.IsNull || !entityId.IsValid || entityId.IsErased)
                return Lang.BBE_Text_DoiTuongKhongConHopLe;

            DBObject? selectedObject = transaction.GetObject(entityId, OpenMode.ForRead, false);
            if (selectedObject is not Entity)
                return Lang.BBE_Text_DoiTuongDuocChonKhongPhaiHinhHocCoTheXoa;

            BlockTableRecord? owner = GetDefinitionOwner(transaction, selectedObject, containerIds);

            if (owner == null || owner.IsLayout)
                return Lang.BBE_Text_DoiTuongKhongThuocMotBlockDefinitionCoTheChinhSua;

            if (owner.IsFromExternalReference || owner.IsFromOverlayReference)
                return Lang.BBE_Text_KhongTheSuaNoiDungXrefOverlayTrongBanVeHienTai;

            LayerTableRecord? layer = transaction.GetObject(((Entity)selectedObject).LayerId,OpenMode.ForRead,false) as LayerTableRecord;

            if (layer != null && layer.IsLocked)
                return Lang.BBE_Text_KhongTheXoaDoiTuongTrenLayerDangKhoa.Replace("{layer.Name}", layer.Name);

            foreach (ObjectId containerId in containerIds ?? System.Array.Empty<ObjectId>())
            {
                BlockReference? blockReference = transaction.GetObject(containerId,OpenMode.ForRead,false) as BlockReference;

                if (blockReference == null)
                    continue;

                if (blockReference.IsDynamicBlock && blockReference.AnonymousBlockTableRecord == owner.ObjectId)
                {
                    return Lang.BBE_Text_DoiTuongThuocBanBieuDienUCuaDynamicBlock + " " +
                           Lang.BBE_Text_KhongTheXoaTrucTiepAnToanHayChuyenBlockVeStatic + " " +
                           Lang.BBE_Text_HoacSuaDynamicBlockDefinitionGoc;
                }

                BlockTableRecord? containerDefinition = transaction.GetObject(blockReference.BlockTableRecord,OpenMode.ForRead,false) as BlockTableRecord;

                if (containerDefinition != null && (containerDefinition.IsFromExternalReference || containerDefinition.IsFromOverlayReference))
                {
                    return Lang.BBE_Text_KhongTheSuaDoiTuongNamTrongXrefOverlay;
                }
            }

            if (IsProtectedSystemAnonymousBlock(owner))
            {
                return Lang.BBE_Text_KhongSuaBlockHeThong.Replace("{owner.Name}", owner.Name);
            }

            if (owner.IsAnonymous && IsDynamicAnonymousRepresentation(transaction, owner))
            {
                return Lang.BBE_Text_DoiTuongThuocBanBieuDienDynamicBlock.Replace("{owner.Name}", owner.Name) + " " +
                       Lang.BBE_Text_KhongTheSuaTrucTiepAnToanHayChuyenBlockVeStatic + " " +
                       Lang.BBE_Text_HoacSuaDynamicBlockDefinitionGoc;
            }

            return null;
        }

        /// Kiểm tra owner có phải anonymous block hệ thống hay không.
        /// *D dùng cho Dimension, *X dùng cho Hatch và *T dùng cho Table.
        private static bool IsProtectedSystemAnonymousBlock(BlockTableRecord owner)
        {
            if (!owner.IsAnonymous)
                return false;

            string name = owner.Name ?? string.Empty;
            return name.StartsWith("*D", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("*X", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("*T", System.StringComparison.OrdinalIgnoreCase);
        }
        /// Kiểm tra owner có phải block *U dùng để hiển thị Dynamic Block hay không.
        private static bool IsDynamicAnonymousRepresentation(Transaction transaction,BlockTableRecord owner)
        {
            foreach (ObjectId referenceId in owner.GetBlockReferenceIds(true, false))
            {
                if (referenceId.IsNull || !referenceId.IsValid || referenceId.IsErased)
                    continue;

                BlockReference? reference = transaction.GetObject(referenceId,OpenMode.ForRead,false) as BlockReference;

                if (reference != null &&
                    reference.IsDynamicBlock &&
                    reference.AnonymousBlockTableRecord == owner.ObjectId)
                {
                    return true;
                }
            }

            return false;
        }

        public readonly struct EraseResult
        {
            public bool Succeeded { get; init; }
            public string? RejectionReason { get; init; }
            public ObjectId DynamicDefinitionId { get; init; }
        }


        /// Xóa một đối tượng con khỏi định nghĩa block đang chứa nó.
        public static EraseResult TryEraseSingleEntity(Transaction transaction,ObjectId entityId,ObjectId[] containerIds)
        {
            string? rejectionReason = ValidateSelection(transaction, entityId, containerIds);
            if (rejectionReason != null)
                return new EraseResult { Succeeded = false, RejectionReason = rejectionReason };

            Entity? entity = transaction.GetObject(entityId, OpenMode.ForWrite, false) as Entity;
            if (entity == null || entity.IsErased)
            {
                return new EraseResult
                {
                    Succeeded = false,
                    RejectionReason = Lang.BBE_Status_DoiTuongKhongConHopLeDaBiXoaHoacNullKhi
                };
            }

            BlockTableRecord? owner = GetDefinitionOwner(transaction, entity, containerIds);

            if (entity is AttributeReference attributeReference)
            {
                if (owner == null)
                {
                    return new EraseResult
                    {
                        Succeeded = false,
                        RejectionReason = Lang.BBE_Status_KhongTimThayDinhNghiaBlockChuaAttribute
                    };
                }

                bool erased = false;
                foreach (ObjectId childId in owner)
                {
                    if (transaction.GetObject(childId, OpenMode.ForWrite, false) is AttributeDefinition definition &&
                        string.Equals(definition.Tag, attributeReference.Tag, System.StringComparison.OrdinalIgnoreCase))
                    {
                        definition.Erase(true);
                        foreach (ObjectId referenceId in owner.GetBlockReferenceIds(true, true))
                        {
                            if (transaction.GetObject(referenceId, OpenMode.ForWrite, false) is not BlockReference reference)
                                continue;

                            foreach (ObjectId attributeId in reference.AttributeCollection)
                            {
                                if (transaction.GetObject(attributeId, OpenMode.ForWrite, false) is AttributeReference referenceAttribute &&
                                    string.Equals(referenceAttribute.Tag, attributeReference.Tag, System.StringComparison.OrdinalIgnoreCase))
                                {
                                    referenceAttribute.Erase(true);
                                }
                            }
                        }

                        erased = true;
                        break;
                    }
                }

                return new EraseResult
                {
                    Succeeded = erased,
                    RejectionReason = erased ? null : Lang.BBE_Status_KhongTimThayAttributeDefinitionTuongUngTrongBlock,
                    DynamicDefinitionId = erased && owner.IsDynamicBlock ? owner.ObjectId : ObjectId.Null
                };
            }

            ObjectId dynamicDefinitionId = (owner != null && owner.IsDynamicBlock) ? owner.ObjectId : ObjectId.Null;

            entity.Erase(true);

            return new EraseResult
            {
                Succeeded = true,
                DynamicDefinitionId = dynamicDefinitionId
            };
        }

        private static BlockTableRecord? GetDefinitionOwner(Transaction transaction,DBObject selectedObject,ObjectId[] containerIds)
        {
            if (selectedObject is AttributeReference)
            {
                BlockReference? blockReference = transaction.GetObject(selectedObject.OwnerId, OpenMode.ForRead, false) as BlockReference;
                if (blockReference != null)
                    return transaction.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                foreach (ObjectId containerId in containerIds ?? System.Array.Empty<ObjectId>())
                {
                    blockReference = transaction.GetObject(containerId, OpenMode.ForRead, false) as BlockReference;
                    if (blockReference != null)
                        return transaction.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;
                }
            }

            return transaction.GetObject(selectedObject.OwnerId, OpenMode.ForRead, false) as BlockTableRecord;
        }

        public static void UpdateDynamicDefinitions(Transaction transaction,IEnumerable<ObjectId> dynamicDefinitionIds)
        {
            foreach (ObjectId definitionId in dynamicDefinitionIds)
            {
                BlockTableRecord? dynamicDefinition = transaction.GetObject(definitionId,OpenMode.ForWrite,false) as BlockTableRecord;

                dynamicDefinition?.UpdateAnonymousBlocks();
            }
        }
    }
}
