using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace clib.Extensions;

public static unsafe class AtkUnitBaseExtensions {
    extension(ref AtkUnitBase atkUnitBase) {
        public bool IsAddonReady() {
            fixed (AtkUnitBase* ptr = &atkUnitBase)
                return ptr != null && ptr->IsVisible && ptr->UldManager.LoadedState == AtkLoadState.Loaded && ptr->IsFullyLoaded();
        }

        public static bool IsAddonReady(string name) {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
            return addon != null && addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded && addon->IsFullyLoaded();
        }

        public static bool CloseAddon(string name) {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
            if (addon == null || !addon->IsVisible)
                return false;
            addon->Close(false);
            return true;
        }
    }

    /// <summary>
    /// porting-note(api13) MUST RE-APPLY: TC-compatible replacement for FFXIVClientStructs'
    /// <c>AtkUnitBase.IsReady</c>, which CS 6966 implements as <c>(Flags1A1 &amp; 1) != 0</c> -- a
    /// hardcoded struct flag at offset 0x1A1. That bit is still 0 at AddonLifecycle PostSetup on the
    /// TC 7.20 binary, so every clib gate written as <c>addon != null &amp;&amp; addon-&gt;IsReady</c> silently
    /// skips its callback: no exception, no log line, the addon simply never advances (observed
    /// 2026-09-10 via YesAlready's SelectString list, which logged a match and then did nothing).
    /// This is the same predicate ECommons' GenericHelpers.IsAddonReady uses, and it is verified to
    /// pass at PostSetup on TC.
    /// </summary>
    public static bool IsReadyOnTC(AtkUnitBase* addon)
        => addon != null && addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded && addon->IsFullyLoaded();

    public static T* GetNodeById<T>(this ref AtkUnitBase addon, uint nodeId) where T : unmanaged
       => addon.UldManager.SearchNodeById<T>(nodeId);

    public static bool TryGetNodeById<T>(this ref AtkUnitBase addon, uint nodeId, out T* node) where T : unmanaged {
        node = addon.UldManager.SearchNodeById<T>(nodeId);
        return node is not null && ((AtkResNode*)node)->IsActuallyVisible;
    }
}
