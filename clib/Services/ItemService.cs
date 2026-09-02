using AllaganLib.GameSheets.Extensions;
using AllaganLib.GameSheets.ItemSources;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace clib.Services;

public sealed unsafe class ItemService : IDisposable {
    public event System.Action? Changed;
    public event System.Action? ArmoireChanged;
    public event System.Action? DresserChanged;

    private readonly ArmoireService _armoire = new();
    private readonly DresserService _dresser = new();

    public ItemService() {
        _armoire.Changed += OnArmoireChanged;
        _dresser.Changed += OnDresserChanged;
        IGameInventory.Get().InventoryChanged += OnInventoryChanged;
    }

    public void Dispose() {
        IGameInventory.Get().InventoryChanged -= OnInventoryChanged;
        _armoire.Changed -= OnArmoireChanged;
        _dresser.Changed -= OnDresserChanged;
        _armoire.Dispose();
        _dresser.Dispose();
    }

    public MirageLocation GetMirageLocation(uint itemId, uint? setItemId = null) {
        itemId = ItemUtil.GetBaseId(itemId).ItemId;
        if (itemId == 0)
            return MirageLocation.None;

        if (_armoire.IsInArmoire(itemId))
            return MirageLocation.Armoire;

        if (setItemId is { } setId and not 0) {
            setId = ItemUtil.GetBaseId(setId).ItemId;
            if (_dresser.GetDresserItemIds().Contains(setId) && DresserService.IsPieceInMirageOutfitSlot(MirageStoreSetItem.GetRow(setId), itemId))
                return MirageLocation.OutfitSlot;
        }
        else if (DresserService.IsPieceInAnyMirageOutfitSlot(itemId)) {
            return MirageLocation.OutfitSlot;
        }

        if (_dresser.IsInDresserLoose(itemId))
            return MirageLocation.DresserLoose;

        if (IsEquipped(itemId))
            return MirageLocation.Equipped;

        if (GetInventoryItemIds().Contains(itemId))
            return MirageLocation.Inventory;

        return MirageLocation.None;
    }

    public bool IsOwned(uint itemId)
        => GetMirageLocation(itemId) is not MirageLocation.None;

    public HashSet<uint> GetInventoryItemIds(params InventoryType[] containers) {
        if (containers is not { Length: > 0 })
            containers = InventoryType.AllPlayer;
        return [.. containers.SelectMany(inv => inv.Items.Where(i => i.ItemId != 0)).Select(item => item.BaseItemId)];
    }

    public HashSet<uint> GetArmoireItemIds() => _armoire.GetArmoireItems();
    public HashSet<uint> GetDresserItemIds() => _dresser.GetDresserItemIds();

    public bool IsCabinetItem(uint itemId) => _armoire.IsCabinetItem(itemId);
    public bool IsInArmoire(uint itemId) => _armoire.IsInArmoire(itemId);
    public bool IsInCabinet(uint itemId) => _armoire.IsInCabinet(itemId);
    public uint ResolveCabinetItemId(uint cacheOrEntryId) => _armoire.ResolveCabinetItemId(cacheOrEntryId);

    public bool IsInDresserLoose(uint itemId, ISet<uint>? outfitTokenIds = null)
        => _dresser.IsInDresserLoose(itemId, outfitTokenIds);

    public bool IsInOutfitSlot(uint pieceId, uint? setItemId = null)
        => _dresser.IsInOutfitSlot(pieceId, setItemId);

    public bool IsFullyDepositedInDresser(uint itemId, ISet<uint>? outfitTokenIds = null)
        => _dresser.IsFullyDepositedInDresser(itemId, outfitTokenIds);

    public bool IsPieceInMirageOutfitSlot(MirageStoreSetItem row, uint pieceItemId)
        => DresserService.IsPieceInMirageOutfitSlot(row, pieceItemId);

    public bool IsPieceInAnyMirageOutfitSlot(uint pieceItemId)
        => DresserService.IsPieceInAnyMirageOutfitSlot(pieceItemId);

    public int GetCount(uint itemId, bool ignoreHq = true) {
        var handle = new ItemHandle(itemId);
        return handle.GetCount(ignoreHq);
    }

    public int GetOwnedCurrencyCount(uint costItemId)
        => CurrencyManager.Instance()->SpecialItemBucket.TryGetValue(costItemId, out var value, true) ? (int)value.Count : InventoryManager.Instance()->GetInventoryItemCount(costItemId);

    /// <remarks>Requires <see cref="CLibModule.SheetManager"/>.</remarks>
    public List<(uint ItemId, uint Amount)> GetItemCosts(uint itemId) {
        itemId = ItemUtil.GetBaseId(itemId).ItemId;
        if (itemId == 0 || Svc.SheetManager is null)
            return [];

        var sources = Svc.SheetManager.ItemInfoCache.GetItemSources(itemId);
        if (sources is not { Count: > 0 })
            return [];

        List<(uint ItemId, uint Amount)> costs = [];
        var seen = new HashSet<(uint, uint)>();
        foreach (var src in sources) {
            if (src is not ItemSource itemSource || !itemSource.Type.IsShop())
                continue;

            var addedFromList = false;
            foreach (var cost in itemSource.CostItems) {
                var costId = cost.ItemId;
                if (costId == 0)
                    continue;
                var amount = cost.Count is > 0 ? cost.Count.Value : 1u;
                if (seen.Add((costId, amount)))
                    costs.Add((costId, amount));
                addedFromList = true;
            }

            if (addedFromList)
                continue;

            if (itemSource.CostItem is { RowId: not 0 } costItem) {
                var amount = itemSource.Quantity is > 0 ? itemSource.Quantity : 1u;
                if (seen.Add((costItem.RowId, amount)))
                    costs.Add((costItem.RowId, amount));
            }
        }

        return costs;
    }

    public bool HasAnyCosts(uint itemId) => GetItemCosts(itemId).Count > 0;

    private void OnArmoireChanged() {
        ArmoireChanged?.Invoke();
        Changed?.Invoke();
    }

    private void OnDresserChanged() {
        DresserChanged?.Invoke();
        Changed?.Invoke();
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events) {
        if (!IClientState.Get().IsLoggedIn || events.Count == 0) return;
        Changed?.Invoke();
    }

    private static bool IsEquipped(uint itemId)
        => InventoryManager.Instance()->GetInventoryItems(InventoryType.EquippedItems)
            .Any(i => i.Value != null && ItemUtil.GetBaseId(i.Value->ItemId).ItemId == itemId);
}
