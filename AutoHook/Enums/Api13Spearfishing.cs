using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AutoHook.Spearfishing.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHook.Enums;

// porting-note(api13) GAP-FILL: upstream's spearfishing support reads the SpearFishing addon
// through FFXIVClientStructs.FFXIV.Client.UI.AddonSpearFishing. CS 6966 has no such type --
// verified against TC_ok/_dalamud_api13/FFXIVClientStructs.dll, which names neither
// AddonSpearFishing nor SpearfishWindow.
//
// Nothing here is measured by this change. The layout is AutoHook's OWN
// Spearfishing/Struct/SpearfishWindow.cs, which this tree already runs against the same addon on
// the same client, re-expressed in the shape upstream's new code expects (a Fish[0..2] indexer
// instead of Fish1/Fish2/Fish3). It cross-checks byte-for-byte against the independently
// maintained mirror in TC_forward/GatherBuddyReborn/GatherBuddy/SeFunctions/SpearfishWindow.cs,
// whose header records it as TC game-7.20-verified:
//   lane base   AutoHook 0x294 + Info.Available 8   == GBR 0x29C + 0x00  -> 0x29C
//   inverse     AutoHook 0x294 + 16                 == GBR 0x29C + 0x08  -> 0x2A4
//   large       AutoHook 0x294 + 17                 == GBR 0x29C + 0x09  -> 0x2A5
//   size        AutoHook 0x294 + 18                 == GBR 0x29C + 0x0A  -> 0x2A6
//   speed       AutoHook 0x294 + 20                 == GBR 0x29C + 0x0C  -> 0x2A8
//   stride      AutoHook 0x2B0-0x294 = 0x1C         == GBR 0x2B8-0x29C = 0x1C
// Two trees, maintained separately, agreeing on every field is the strongest evidence available
// short of a live read, so the lanes are used rather than B1'd.
//
// GaugeBar resolves by UldManager node index (35), not by a struct offset, exactly as AutoHook's
// own struct already does -- node indices are stable across the 7.2/7.5 gap in a way raw offsets
// are not.
//
// RUNTIME-VERIFY: not yet exercised through AutoHook's own spearfishing path on a live TC client.
// Blast radius is small by construction: the only consumers of lane data are the debug tab's
// FormatFishInfo and the replay writer -- AutoHook takes no action on it -- while the thing the
// feature actually gates on, SpearfishingInfo.WindowOpen, comes from the addon pointer and
// AtkUnitBase.WindowNode and needs no offset at all.
[StructLayout(LayoutKind.Explicit, Size = 0x2F0)]
public unsafe struct AddonSpearFishing
{
    [FieldOffset(0x000)] public AtkUnitBase AtkUnitBase;

    /// <summary>Lane 0..2. First lane at 0x29C, stride 0x1C.</summary>
    public FishInfo* Fish => (FishInfo*)((byte*)Unsafe.AsPointer(ref this) + 0x29C);

    public AtkComponentGaugeBar* GaugeBar => (AtkComponentGaugeBar*)AtkUnitBase.UldManager.NodeList[35];

    [StructLayout(LayoutKind.Explicit, Size = 0x1C)]
    public struct FishInfo
    {
        [FieldOffset(0x00)] public bool Available;
        [FieldOffset(0x08)] public bool InverseDirection;
        [FieldOffset(0x09)] public bool GuaranteedLarge;
        [FieldOffset(0x0A)] public SpearfishSize Size;
        // Upstream types this short and round-trips it as ReadInt16; AutoHook's own struct types
        // the same two bytes as the ushort-backed SpearfishSpeed. Kept as short so upstream's
        // replay reader and debug formatter compile unchanged.
        [FieldOffset(0x0C)] public short Speed;
    }
}
