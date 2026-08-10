namespace AutoHook.Enums;

// porting-note(api13): types upstream takes from FFXIVClientStructs that do not exist in the
// version this generation ships (CS 6966). Verified absent by reading the metadata of
// TC_ok/_dalamud_api13/FFXIVClientStructs.dll rather than assumed.

/// <summary>
/// Upstream reads this from CS as <c>(FishingHookStrength)actionTimelineId</c>. CS 6966 has no
/// such enum, but the ids are the same ones this tree's own <see cref="BiteType"/> already
/// documents (Weak/Strong/Legendary = 36/37/38), which is where these values come from — they are
/// not invented. Kept as a separate type so the vendored WorldState code compiles unchanged.
/// </summary>
public enum FishingHookStrength : ushort
{
    None = 0,
    Weak = 36,
    Strong = 37,
    Legendary = 38,
}

/// <summary>
/// Upstream reads this off <c>FishingEventHandler.CurrentCastBaitFlags</c>, a field CS 6966's
/// FishingEventHandler does not have. On this generation the value is synthesised from the pin's
/// own detection (<c>BaitManager.IsMooching()</c> / <c>PlayerRes.IsMoochAvailable()</c>) instead
/// of being read from game memory, so these bits are internal to this build only — do NOT treat
/// them as the game's own flag values.
/// </summary>
[Flags]
public enum FishingBaitFlags : byte
{
    None = 0,
    Mooch = 1 << 0,
    Swimbait = 1 << 1,
}

/// <summary>
/// Upstream uses <c>WksMissionRank</c>; CS 6966's WKSMissionModule has no such
/// nested type. Cosmic Exploration mission rank is only consumed by CosmicMissionScoreCD, which no
/// community preset in the measured corpus uses, so this stays at <see cref="None"/> on this
/// generation (B1) rather than being read from a guessed offset.
/// </summary>
public enum WksMissionRank : byte
{
    None = 0,
}

/// <summary>
/// Constants for FishingState members CS 6966 leaves unnamed.
/// </summary>
public static class Api13FishingState
{
    /// <summary>
    /// Upstream's <c>FishingState.ModestLure</c>. CS 6966 declares no name for value 10 — it is a
    /// gap in an otherwise contiguous run — while the newer FFXIVClientStructs assembly names it
    /// ModestLure, and every other member of the enum carries an identical value in both builds.
    /// The state itself does exist on this client: the Modest Lure action (37595) is populated on
    /// the TC v7.20 Action sheet, checked against HoshinoLYK/ffxiv-datamining-tc @ 8076e39. So the
    /// only thing missing is the NAME in the CS build we compile against, which is why this is a
    /// measured constant rather than a guess — and why dropping the branch instead would silently
    /// stop AutoLures from firing during modest-lure fishing.
    /// </summary>
    public const FFXIVClientStructs.FFXIV.Client.Game.Event.FishingState ModestLure =
        (FFXIVClientStructs.FFXIV.Client.Game.Event.FishingState)10;
}
