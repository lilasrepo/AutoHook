using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Collections.Frozen;

namespace clib.Services;

internal sealed unsafe class ArmoireService : IDisposable {
    public event Action? Changed;

    private static readonly IEnumerable<Sheets.Cabinet> CabinetRows = Sheets.Cabinet.Where(r => r.RowId > 0 && r.Item.RowId > 0);
    private static readonly FrozenDictionary<uint, List<uint>> CabinetRowsByItemId = CabinetRows.GroupBy(r => r.Item.RowId).ToFrozenDictionary(g => g.Key, g => g.Select(r => r.RowId).ToList());
    private static readonly FrozenDictionary<uint, uint> CabinetByRowId = CabinetRows.ToFrozenDictionary(r => r.RowId, r => r.Item.RowId);
    private readonly HashSet<uint> _ownedItemIds = [];

    public ArmoireService() {
        IClientState.Get().Login += OnLogin;
        IClientState.Get().Logout += OnLogout;
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostRequestedUpdate, "Cabinet", OnCabinetRefresh);

        if (IClientState.Get().IsLoggedIn)
            RefreshCache();
    }

    public void Dispose() {
        IAddonLifecycle.Get().UnregisterListener(AddonEvent.PostRequestedUpdate, "Cabinet", OnCabinetRefresh);
        IClientState.Get().Logout -= OnLogout;
        IClientState.Get().Login -= OnLogin;
        _ownedItemIds.Clear();
    }

    public void RefreshCache() {
        GameMain.ExecuteCommand((int)CommandFlag.RequestCabinet);
        BuildCache(notify: true);
    }

    public HashSet<uint> GetArmoireItems() {
        BuildCache(notify: false);
        return [.. _ownedItemIds];
    }

    public bool IsCabinetItem(uint itemId)
        => ItemUtil.GetBaseId(itemId).ItemId is var baseId and not 0 && CabinetRowsByItemId.ContainsKey(baseId);

    public bool IsInArmoire(uint itemId) {
        itemId = ResolveCabinetItemId(itemId);
        if (itemId == 0)
            return false;
        BuildCache(notify: false);
        return _ownedItemIds.Contains(itemId) || IsInCabinet(itemId);
    }

    public bool IsInCabinet(uint itemId)
        => ItemUtil.GetBaseId(itemId).ItemId is var baseId and not 0 && CabinetRowsByItemId.TryGetValue(baseId, out var rowIds) && rowIds.Any(IsCabinetItemCollected);

    public uint ResolveCabinetItemId(uint cacheOrEntryId) {
        if (cacheOrEntryId == 0)
            return 0;
        var baseId = ItemUtil.GetBaseId(cacheOrEntryId).ItemId;
        if (CabinetRowsByItemId.ContainsKey(baseId))
            return baseId;
        if (CabinetByRowId.TryGetValue(cacheOrEntryId, out var fromEntry) || CabinetByRowId.TryGetValue(baseId, out fromEntry))
            return ItemUtil.GetBaseId(fromEntry).ItemId;
        return baseId;
    }

    private static bool IsCabinetItemCollected(uint cabinetRowId) {
        var uiState = UIState.Instance();
        return uiState is not null && uiState->Cabinet.IsCabinetLoaded() && uiState->Cabinet.IsItemInCabinet(cabinetRowId) || IsItemFinderBitSet(cabinetRowId);
    }

    private static bool IsItemFinderBitSet(uint cabinetRowId) {
        if (cabinetRowId < 1048)
            return false;

        var finder = ItemFinderModule.Instance();
        if (finder is null)
            return false;

        var bits = finder->CabinetItemUnlockBits;
        var (wordIndex, bitOffset) = Math.DivRem(cabinetRowId - 1048, 32u);
        return wordIndex < (uint)bits.Length && (bits[(int)wordIndex] & (1u << (int)bitOffset)) != 0;
    }

    private void OnLogin() => RefreshCache();
    private void OnLogout(int _, int __) => ClearCache();

    private void ClearCache() {
        var hadAny = _ownedItemIds.Count > 0;
        _ownedItemIds.Clear();
        if (hadAny)
            Changed?.Invoke();
    }

    private void OnCabinetRefresh(AddonEvent _, AddonArgs __) => BuildCache(notify: true);

    private void BuildCache(bool notify) {
        if (!IClientState.Get().IsLoggedIn) {
            ClearCache();
            return;
        }

        HashSet<uint> nextOwned = [.. CabinetRowsByItemId.Where(kv => kv.Value.Any(IsCabinetItemCollected)).Select(kv => kv.Key)];
        var changed = !_ownedItemIds.SetEquals(nextOwned);
        _ownedItemIds.Clear();
        _ownedItemIds.UnionWith(nextOwned);

        if (notify && changed)
            Changed?.Invoke();
    }
}
