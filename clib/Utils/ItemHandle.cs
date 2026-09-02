using clib.Services;
using Dalamud.Game.Inventory;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Excel;
using FFXIVClientStructs.Interop;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;

namespace clib.Utils;

// https://github.com/Haselnussbomber/HaselCommon/blob/962b2ac2adaecd59d3fa541bb544bd4ae3a144e9/HaselCommon/Utils/ItemHandle.cs
public class ItemHandle {
    public ItemHandle(uint itemId) {
        ItemId = itemId;
        unsafe {
            ExcelRow = ItemUtil.IsEventItem(ItemId) ? Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(124, ItemId)
                : Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemUtil.GetBaseId(ItemId).ItemId);
        }
    }

    public ItemHandle(ItemLocation itemLocation) {
        ItemLocation = itemLocation;
        unsafe {
            var inventoryItem = itemLocation.GetInventoryItem();
            ItemId = inventoryItem != null ? inventoryItem->GetItemId() : 0;
            ExcelRow = ItemUtil.IsEventItem(ItemId) ? Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(124, ItemId)
                : Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemUtil.GetBaseId(ItemId).ItemId);
        }
    }

    public static unsafe implicit operator ItemHandle(InventoryItem* item) => new((item->Container, (ushort)item->Slot));
    public static unsafe implicit operator ItemHandle(Pointer<InventoryItem> item) => new((item.Value->Container, (ushort)item.Value->Slot));
    public static implicit operator ItemHandle(InventoryItem item) => new((item.Container, (ushort)item.Slot));
    public static implicit operator ItemHandle(GameInventoryItem item) => new(((InventoryType)item.ContainerType, (ushort)item.InventorySlot));
    public static implicit operator ItemHandle(Item item) => new(item.RowId);
    public static implicit operator ItemHandle(RowRef<Item> rowRef) => new(rowRef.RowId);
    public static implicit operator ItemHandle(EventItem eventItem) => new(eventItem.RowId);
    public static implicit operator ItemHandle(RowRef<EventItem> rowRef) => new(rowRef.RowId);
    public static implicit operator ItemHandle(ItemLocation itemLocation) => new(itemLocation);
    public static implicit operator ItemHandle(uint itemId) => new(itemId);
    public static implicit operator uint(ItemHandle itemInfo) => itemInfo.ItemId;
    public static unsafe implicit operator Pointer<InventoryItem>(ItemHandle handle) => handle.ItemLocation != null ? InventoryManager.Instance()->GetInventorySlot(handle.ItemLocation.Container, handle.ItemLocation.Slot) : null;

    public uint ItemId { get; }
    public ItemLocation? ItemLocation { get; set; }
    public unsafe ExcelRow* ExcelRow { get; }

    [MemberNotNullWhen(true, nameof(ItemLocation))]
    public unsafe bool TrySetItemLocation(InventoryItem.ItemFlags requiredFlag = InventoryItem.ItemFlags.None) {
        if (ItemLocation is not null) return true;
        foreach (var inv in InventoryType.FullInventory) {
            if (InventoryManager.Instance()->GetInventoryItems(inv).FirstOrDefault(i => i.Value != null && i.Value->ItemId == ItemId && (requiredFlag is InventoryItem.ItemFlags.None || i.Value->Flags.HasFlag(requiredFlag)))
                is { } item && item.Value != null) {
                ItemLocation = new ItemLocation(inv, item.Value->GetSlot());
                return true;
            }
        }
        return false;
    }

    public RowRef<Item> GameData => IDataManager.Get().GetRef<Item>(ItemUtil.GetBaseId(ItemId).ItemId);
    public bool IsValid => ItemId is not 0;

    public uint BaseItemId => ItemUtil.GetBaseId(ItemId).ItemId;
    public ItemKind ItemKind => ItemUtil.GetBaseId(ItemId).Kind;
    public bool IsNormalItem => ItemUtil.IsNormalItem(ItemId);
    public bool IsCollectible => ItemUtil.IsCollectible(ItemId);
    public bool IsHighQuality => ItemUtil.IsHighQuality(ItemId);
    public bool IsEventItem => ItemUtil.IsEventItem(ItemId);
    public bool IsTreasureMap => IsEventItem ? EventItem.GetRow(ItemId).Category.RowId == 2 : GameData.ValueNullable?.FilterGroup == 18;

    public bool HasItem => GetCount() > 0;
    public unsafe bool IsEquipped => InventoryManager.Instance()->GetInventoryItems(InventoryType.EquippedItems).Any(i => i.Value != null && i.Value->ItemId == ItemId);

    public unsafe bool InGearset {
        get {
            var gm = RaptureGearsetModule.Instance();
            for (byte i = 0; i < 100; ++i) {
                if (!gm->IsValidGearset(i)) continue;
                var gearset = gm->GetGearset(i);
                if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                    if (gearset->Items.ToArray().Any(x => ItemUtil.GetBaseId(x.ItemId).ItemId == ItemId)) return true;
            }
            return false;
        }
    }

    /// <remarks>
    /// Returns -1 if the item is not found
    /// </remarks>
    public unsafe float Condition {
        get {
            if (ItemLocation is null && !TrySetItemLocation()) return -1;
            var im = InventoryManager.Instance();
            if (im is null) return -1;
            var slot = im->GetInventorySlot(ItemLocation.Container, ItemLocation.Slot);
            return slot is not null && slot->ItemId != 0 ? slot->Condition : -1;
        }
    }

    public bool IsRepairable => Condition is not -1 and < 30000f;

    public unsafe int GetCount(bool ignoreHq = true)
        => ignoreHq ? InventoryManager.Instance()->GetInventoryItemCount(BaseItemId) + InventoryManager.Instance()->GetInventoryItemCount(ItemId, true)
        : InventoryManager.Instance()->GetInventoryItemCount(ItemId);

    public unsafe bool LowerItemQuality() {
        if (ItemLocation is null) return false;
        if (RaptureAtkModule.Instance()->AgentUpdateFlag.HasFlag(RaptureAtkModule.AgentUpdateFlags.InventoryUpdate)) return false;
        if (!ICondition.Get().CanLowerItemQuality()) return false;
        var item = InventoryManager.Instance()->GetInventorySlot(ItemLocation.Container, ItemLocation.Slot);
        if (!item->IsHighQuality()) return true;
        AgentInventoryContext.Instance()->LowerItemQuality(item, ItemLocation.Container, ItemLocation.Slot, 0);
        return true;
    }

    // porting-note(api13): CanEquip removed - its equip-eligibility check needs
    // ClassJobCategory.HasJobsAtLevel, which is gone on this generation (see clib.csproj's Compile
    // Remove group for the underlying Lumina.Excel.Sheets shape drift). Zero callers in the
    // vendored tree; Equip() below documents "check CanEquip first" but nothing here calls it
    // either, so it is left standing rather than chased.

    // porting-note(api13): Equip() removed alongside CanEquip() (its own doc comment says "be sure
    // to check CanEquip first" - they are one pair) - it also needs Item.EquipSlot, which lived in
    // the now-removed Extensions/Lumina/ItemExtensions.cs (see clib.csproj). Zero callers.

    public unsafe bool OpenContext() {
        var agent = AgentInventoryContext.Instance();
        if (agent == null || ItemLocation == null) return false;

        agent->OpenForItemSlot(ItemLocation.Container, ItemLocation.Slot, 0, AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->GetAddonId());
        return true;
    }

    public unsafe void MoveTo(InventoryType[] containers) {
        foreach (var cont in containers) {
            if (InventoryManager.Instance()->GetFirstEmptySlot(cont) is { } slot) {
                MoveTo(cont, (ushort)slot);
            }
        }
    }

    private unsafe void MoveTo(InventoryType cont) {
        if (InventoryManager.Instance()->GetFirstEmptySlot(cont) is { } slot) {
            MoveTo(cont, (ushort)slot);
        }
    }

    private unsafe void MoveTo(InventoryType cont, ushort slot) {
        if (ItemLocation is null) return;
        InventoryManager.Instance()->MoveItemSlot(ItemLocation.Container, ItemLocation.Slot, cont, slot, true);
    }

    // porting-note(api13): Locate/IsInArmoire/IsInDresserLoose/IsInOutfitSlot/MirageLocation/
    // GetCosts/HasAnyCosts all removed - they only exist to read Svc.Items (clib's Items service),
    // which is cut on this generation (see clib.csproj's Compile Remove group). Zero callers in the
    // vendored tree.

    public override string ToString() => IsValid ? $"[#{ItemId}] {GameData.Value.Name}" : $"{nameof(ItemHandle)}#Invalid";
}
