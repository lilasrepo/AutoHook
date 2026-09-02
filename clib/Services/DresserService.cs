using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace clib.Services;

internal sealed unsafe class DresserService : IDisposable {
    public event System.Action? Changed;

    private HashSet<uint> _lastNotifiedIds = [];

    public DresserService() {
        IClientState.Get().Login += OnLogin;
        IClientState.Get().Logout += OnLogout;
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostRequestedUpdate, "MiragePrismPrismBox", OnPrismBoxRefresh);

        if (IClientState.Get().IsLoggedIn)
            NotifyIfChanged();
    }

    public void Dispose() {
        IAddonLifecycle.Get().UnregisterListener(AddonEvent.PostRequestedUpdate, "MiragePrismPrismBox", OnPrismBoxRefresh);
        IClientState.Get().Logout -= OnLogout;
        IClientState.Get().Login -= OnLogin;
        _lastNotifiedIds = [];
    }

    public HashSet<uint> GetDresserItemIds() {
        var finder = ItemFinderModule.Instance();
        return finder is null ? [] : [.. finder->GlamourDresserBaseItemIds];
    }

    public bool IsInDresserLoose(uint itemId, ISet<uint>? outfitTokenIds = null) {
        itemId = ItemUtil.GetBaseId(itemId).ItemId;
        if (itemId == 0)
            return false;
        var dresser = GetDresserItemIds();
        if (!dresser.Contains(itemId))
            return false;
        if (outfitTokenIds is null)
            return !IsMirageSetToken(itemId);
        return !outfitTokenIds.Contains(itemId);
    }

    public bool IsInOutfitSlot(uint pieceItemId, uint? setItemId = null) {
        pieceItemId = ItemUtil.GetBaseId(pieceItemId).ItemId;
        if (pieceItemId == 0)
            return false;

        if (setItemId is { } setId and not 0) {
            setId = ItemUtil.GetBaseId(setId).ItemId;
            var row = MirageStoreSetItem.GetRow(setId);
            return IsPieceInMirageOutfitSlot(row, pieceItemId);
        }

        return IsPieceInAnyMirageOutfitSlot(pieceItemId);
    }

    public bool IsFullyDepositedInDresser(uint itemId, ISet<uint>? outfitTokenIds = null) {
        itemId = ItemUtil.GetBaseId(itemId).ItemId;
        if (itemId == 0)
            return false;

        var dresser = GetDresserItemIds();
        if (dresser.Contains(itemId) && (outfitTokenIds is null ? !IsMirageSetToken(itemId) : !outfitTokenIds.Contains(itemId)))
            return true;

        var inAnyOutfit = false;
        foreach (var row in MirageStoreSetItem.Where(r => r.RowId > 0)) {
            if (!row.Items.Any(itemRef => itemRef.RowId != 0 && itemRef.RowId == itemId))
                continue;
            inAnyOutfit = true;
            if (!IsPieceInMirageOutfitSlot(row, itemId))
                return false;
        }
        return inAnyOutfit;
    }

    public static bool IsPieceInMirageOutfitSlot(MirageStoreSetItem row, uint pieceItemId)
        => row.Items.Select((itemRef, slotIndex) => (itemRef, slotIndex))
            .Any(x => x.itemRef.RowId != 0 && x.itemRef.RowId == pieceItemId && row.IsSetSlotCollected(x.slotIndex));

    public static bool IsPieceInAnyMirageOutfitSlot(uint pieceItemId)
        => MirageStoreSetItem.Where(r => r.RowId > 0).Any(r => IsPieceInMirageOutfitSlot(r, pieceItemId));

    private static bool IsMirageSetToken(uint itemId)
        => MirageStoreSetItem.TryGetRow(itemId, out var row) && row.RowId > 0;

    private void OnLogin() => NotifyIfChanged();

    private void OnLogout(int _, int __) {
        if (_lastNotifiedIds.Count == 0)
            return;
        _lastNotifiedIds = [];
        Changed?.Invoke();
    }

    private void OnPrismBoxRefresh(AddonEvent _, AddonArgs __) => NotifyIfChanged();

    private void NotifyIfChanged() {
        var next = GetDresserItemIds();
        if (_lastNotifiedIds.SetEquals(next))
            return;
        _lastNotifiedIds = next;
        IPluginLog.Get().Debug($"[{nameof(DresserService)}] Dresser changed.");
        Changed?.Invoke();
    }
}
