using AutoHook.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using AutoHook.SeFunctions;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using System.Reflection;
using AchievementStruct = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using FishingState = FFXIVClientStructs.FFXIV.Client.Game.Event.FishingState;

namespace AutoHook;

public readonly struct BiteContext {
    public double BiteTimeSeconds { get; init; }
    public bool ChumActive { get; init; }
    public IntuitionStatus IntuitionStatus { get; init; }
    public float IntuitionTimeRemaining { get; init; }
    public SpectralCurrentStatus SpectralCurrentStatus { get; init; }
    public uint? LastCaughtFishId { get; init; }
}

public sealed class WorldStateUpdater : IDisposable {
    private const float BiteTimeLogThreshold = 0.25f;

    // porting-note(api13): the bite and catch feeds are wired HERE rather than from FishingManager.
    // Upstream drives them from two hooks CS 6966 cannot express - AgentCatch.UpdateCatch (the type
    // is absent) and FishingEventHandler's PlayAnimation vtable slot (the slot is absent). The
    // replacements are this tree's own runtime-verified mechanisms:
    //   catch -> the UpdateCatch SIGNATURE hook (SignaturePatterns.UpdateCatch, transplanted from
    //            GatherBuddyReborn's 7.3 rescan and runtime-verified in session 15). Its parameter
    //            list is identical to upstream's AgentCatch one, param for param.
    //   bite  -> SeTugType, a static-address read, sampled on the transition into FishingState.Bite.
    // Upstream's third hook, PacketDispatcher.HandleActorControlPacket, is also absent from CS
    // 6966; it only set _needInventoryUpdate on two Cosmic Exploration ActorControl categories,
    // and Dalamud's InventoryChanged event still covers that refresh (just less promptly), so it
    // is dropped rather than approximated.
    // Keeping both inside WorldStateUpdater means nothing outside has to remember to call them -
    // an earlier revision exposed NotifyBite/NotifyCatch for FishingManager to call, and when the
    // vendored HEAD FishingManager replaced this build's, both silently lost their only caller and
    // the whole bite/catch feed went dead while still compiling clean.
    private unsafe delegate void UpdateCatchDelegate(IntPtr module, uint fishId, bool large, ushort size,
        byte amount, byte level, byte stars, byte oceanStars, bool isMoochable, bool isFirstTimeCatch,
        byte a11, byte a12);

    private readonly Hook<UpdateCatchDelegate>? _updateCatchHook;
    private readonly SeTugType? _tugType;
    private FishingState _lastNotifiedState = FishingState.NotFishing;
    private readonly Hook<ActionManager.Delegates.UseAction>? _useActionHook;
    private readonly Hook<AchievementStruct.Delegates.ReceiveAchievementProgress>? _receiveAchievementProgressHook;
    // porting-note(api13): upstream's `FshActions = ClassJob.Get(18).GetActions()` was dropped. Both
    // helpers come from an ECommons revision newer than the one pinned here, and the field is
    // write-only even upstream - nothing ever reads it (BuildTrackedFishingActions reflects over
    // IDs.Actions instead). Nothing is lost by not reimplementing them.
    private static readonly (uint Id, ActionType Type)[] TrackedFishingActions = BuildTrackedFishingActions();
    private static readonly (uint Id, ActionType Type)[] TrackedAutoCastItems =
    [
        (IDs.Item.HiCordial, ActionType.Item),
        (IDs.Item.HQCordial, ActionType.Item),
        (IDs.Item.Cordial, ActionType.Item),
        (IDs.Item.HQWateredCordial, ActionType.Item),
        (IDs.Item.WateredCordial, ActionType.Item),
    ];

    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly long _startQpc;
    private readonly Dictionary<uint, (float Time, int Stacks)> _statusScratch = [];
    private readonly List<uint> _swimbaitScratch = [];
    private readonly List<ulong> _partyScratch = [];
    private readonly List<InstanceContentOceanFishing.FishDataStruct> _fishDataScratch = [];
    private readonly Cooldown[] _cooldownScratch = new Cooldown[PlayerInfo.NumCooldownGroups];
    private readonly Dictionary<ulong, uint> _actionStatusScratch = [];
    private readonly Dictionary<ulong, int> _actionRecastScratch = [];
    private readonly Dictionary<uint, ushort> _dutyChargesScratch = [];
    private readonly Dictionary<uint, int> _spearItemCountScratch = [];

    private bool _needInventoryUpdate = true;
    private bool _spearItemCountsReady;

    public unsafe WorldStateUpdater() {
        _startQpc = Framework.Instance()->PerformanceCounterValue;
        _useActionHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseAction>((nint)ActionManager.MemberFunctionPointers.UseAction, UseActionDetour);
        _receiveAchievementProgressHook = Svc.Hook.HookFromAddress<AchievementStruct.Delegates.ReceiveAchievementProgress>((nint)AchievementStruct.MemberFunctionPointers.ReceiveAchievementProgress, ReceiveAchievementProgressDetour);
        try {
            _updateCatchHook = Svc.Hook.HookFromSignature<UpdateCatchDelegate>(
                SignaturePatterns.UpdateCatch, UpdateCatchDetour);
            _tugType = new SeTugType(Svc.SigScanner);
        }
        catch (Exception e) {
            // A signature that no longer resolves must degrade, not kill the plugin.
            Svc.Log.Error(e, "[WorldStateUpdater] catch/tug feed unavailable - fish counting and tug type will not update.");
        }

        _useActionHook?.Enable();
        _updateCatchHook?.Enable();
        _receiveAchievementProgressHook?.Enable();
        Svc.GameInventory.InventoryChanged += OnInventoryChanged;
    }

    public void Dispose() {
        _useActionHook?.Dispose();
        _updateCatchHook?.Dispose();
        _receiveAchievementProgressHook?.Dispose();
        Svc.GameInventory.InventoryChanged -= OnInventoryChanged;
    }

    // push current game state into WorldState. call every frame.
    public unsafe void Update() {
        if (Svc.ClientState.LocalPlayer?.ClassJob.RowId is not 18 || Svc.ClientState.LocalPlayer is null)
            return;

        var ws = Service.WorldState;
        var fwk = Framework.Instance();
        if (fwk == null)
            return;

        ws.Execute(new WorldState.OpFrameStart(new FrameState(
            _startTime.AddSeconds((double)(fwk->PerformanceCounterValue - _startQpc) / ws.QPF),
            (ulong)fwk->PerformanceCounterValue,
            fwk->FrameCounter,
            fwk->RealFrameDeltaTime,
            fwk->FrameDeltaTime,
            fwk->GameSpeedMultiplier)));

        var lp = Svc.ClientState.LocalPlayer;
        var gp = lp?.CurrentGp ?? 0;
        var maxGp = lp?.MaxGp ?? 0;
        if (ws.Player.CurrentGp != gp || ws.Player.MaxGp != maxGp)
            ws.Execute(new PlayerInfo.OpGp(gp, maxGp));

        var level = lp?.Level ?? 0;
        if (ws.Player.Level != level)
            ws.Execute(new PlayerInfo.OpLevel(level));

        UpdateEorzeaTime(ws, fwk);
        UpdateStatuses(ws);
        UpdateCooldowns(ws);
        UpdateActionStates(ws);
        UpdateDutyActions(ws);
        UpdatePartyAndInstance(ws);
        UpdateOceanFishing(ws);
        UpdateWKS(ws);
        UpdateTerritory(ws);
        UpdateWeather(ws);

        var previousFishingState = ws.Fishing.FishingState;
        var biteContext = CollectBiteContext(ws);
        UpdateFishingState(ws, biteContext);

        // sample the tug the moment the state enters Bite - SeTugType is a live read, so it has to be
        // taken on the transition, exactly when upstream's PlayAnimation hook would have fired.
        if (_lastNotifiedState != FishingState.Bite && ws.Fishing.FishingState == FishingState.Bite)
            NotifyBite(_tugType?.Bite ?? BiteType.Unknown);
        _lastNotifiedState = ws.Fishing.FishingState;

        if (previousFishingState == FishingState.NotFishing && ws.Fishing.FishingState != FishingState.NotFishing)
            ws.Execute(new WorldState.OpBeganSession());
        else if (previousFishingState != FishingState.NotFishing && ws.Fishing.FishingState == FishingState.NotFishing)
            ws.Execute(new WorldState.OpEndedSession());

        if (_needInventoryUpdate) {
            var (counts, stats) = CollectInventory();
            ws.Execute(counts);
            if (ws.Player.FreeInventorySlots != stats.FreeSlots || ws.Player.ReduceableFishCount != stats.ReduceableFish)
                ws.Execute(stats);
            _needInventoryUpdate = false;
            ProcessSpearfishingCatches(ws);
        }

        UpdateSwimbaitIds(ws);
        UpdatePotCooldown(ws);
        UpdateBiteContext(ws, biteContext);
        UpdateIntuition(ws, biteContext);
        UpdateSpearfishing(ws);

        var fishingState = ws.Fishing.FishingState;
        if (ShouldCaptureCastSnapshot(previousFishingState, fishingState))
            ws.Execute(new FishingInfo.OpUpdateCastSnapshot(previousFishingState));
        else if (ws.Fishing.CastSnapshot.Active && fishingState is not Api13FishingState.LineInWater and not FishingState.Bite)
            ws.Execute(new FishingInfo.OpInvalidateCastSnapshot());
    }

    private static bool ShouldCaptureCastSnapshot(FishingState previous, FishingState current)
        => previous != Api13FishingState.LineInWater && current == Api13FishingState.LineInWater;

    public void RefreshFishingStateSnapshot() {
        if (Svc.ClientState.LocalPlayer?.ClassJob.RowId is not 18 || Svc.ClientState.LocalPlayer is null)
            return;

        var ws = Service.WorldState;
        var biteContext = CollectBiteContext(ws);
        UpdateFishingState(ws, biteContext);
        UpdateSwimbaitIds(ws);
        UpdateBiteContext(ws, biteContext);
        UpdateIntuition(ws, biteContext);
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> _)
        => _needInventoryUpdate = true;

    private static unsafe void UpdateEorzeaTime(WorldState ws, Framework* fwk) {
        var eorzea = TimeOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(fwk->ClientTime.EorzeaTime).DateTime);
        if (ws.EorzeaTime != eorzea)
            ws.Execute(new WorldState.OpEorzeaTime(eorzea));
    }

    private void UpdateStatuses(WorldState ws) {
        _statusScratch.Clear();
        if (Svc.ClientState.LocalPlayer is { StatusList: var statuses }) {
            foreach (var buff in statuses)
                _statusScratch[buff.StatusId] = (buff.RemainingTime, buff.Param);
        }

        if (StatusesEqual(ws.Player.Statuses, _statusScratch))
            return;

        ws.Execute(new PlayerInfo.OpStatuses(new Dictionary<uint, (float, int)>(_statusScratch)));
    }

    private static bool StatusesEqual(Dictionary<uint, (float Time, int Stacks)> current, Dictionary<uint, (float Time, int Stacks)> next) {
        if (current.Count != next.Count)
            return false;

        foreach (var (id, (time, stacks)) in next) {
            if (!current.TryGetValue(id, out var prev))
                return false;
            if (prev.Stacks != stacks)
                return false;
            if (Math.Abs(prev.Time - time) > 1f)
                return false;
        }

        return true;
    }

    private unsafe void UpdateCooldowns(WorldState ws) {
        var am = ActionManager.Instance();
        if (am == null)
            return;

        var anyNonDefault = false;
        for (var i = 0; i < PlayerInfo.NumCooldownGroups; i++) {
            var detail = am->GetRecastGroupDetail(i);
            if (detail == null) {
                _cooldownScratch[i] = default;
                continue;
            }

            _cooldownScratch[i] = new Cooldown(detail->Elapsed, detail->Total);
            if (detail->Total > 0f)
                anyNonDefault = true;
        }

        if (!anyNonDefault && ws.Player.Cooldowns.All(c => c.Total <= 0f))
            return;

        if (MemoryExtensions.SequenceEqual(ws.Player.Cooldowns.AsSpan(), _cooldownScratch.AsSpan()))
            return;

        if (_cooldownScratch.AsSpan().IndexOfAnyExcept(default(Cooldown)) < 0) {
            ws.Execute(new PlayerInfo.OpCooldown(true, []));
            return;
        }

        var changes = CalcCooldownDifference(_cooldownScratch, ws.Player.Cooldowns);
        if (changes.Count > 0)
            ws.Execute(new PlayerInfo.OpCooldown(false, changes));
    }

    private static (uint Id, ActionType Type)[] BuildTrackedFishingActions()
        => [.. typeof(IDs.Actions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => Convert.ToUInt32(f.GetValue(null) ?? 0u))
            .Where(id => id != IDs.Actions.None)
            .Select(id => (id, ActionType.Action))];

    private unsafe void UpdateActionStates(WorldState ws) {
        var am = ActionManager.Instance();
        if (am == null)
            return;

        _actionStatusScratch.Clear();
        _actionRecastScratch.Clear();
        foreach (var (id, type) in TrackedFishingActions.Concat(TrackedAutoCastItems)) {
            var key = PlayerInfo.ActionKey(type, id);
            _actionStatusScratch[key] = am->GetActionStatus(type, id);
            _actionRecastScratch[key] = am->GetRecastGroup((int)type, id);
        }

        if (ActionStatesEqual(ws.Player.ActionStatus, ws.Player.ActionRecastGroup, _actionStatusScratch, _actionRecastScratch))
            return;

        ws.Execute(new PlayerInfo.OpActionStates(new Dictionary<ulong, uint>(_actionStatusScratch), new Dictionary<ulong, int>(_actionRecastScratch)));
    }

    private static bool ActionStatesEqual(Dictionary<ulong, uint> currentStatus, Dictionary<ulong, int> currentGroups, Dictionary<ulong, uint> nextStatus, Dictionary<ulong, int> nextGroups) {
        if (currentStatus.Count != nextStatus.Count || currentGroups.Count != nextGroups.Count)
            return false;

        foreach (var (key, status) in nextStatus) {
            if (!currentStatus.TryGetValue(key, out var prevStatus) || prevStatus != status)
                return false;
            if (!currentGroups.TryGetValue(key, out var prevGroup) || prevGroup != nextGroups[key])
                return false;
        }

        return true;
    }

    private unsafe void UpdateDutyActions(WorldState ws) {
        _dutyChargesScratch.Clear();
        var dm = DutyActionManager.GetInstanceIfReady();
        var active = dm != null;
        if (dm != null) {
            for (var i = 0; i < dm->NumValidSlots; i++) {
                var id = dm->ActionId[i];
                if (id == 0)
                    continue;
                _dutyChargesScratch[id] = dm->CurCharges[i];
            }
        }

        if (ws.Player.DutyActionManagerActive == active && DutyChargesEqual(ws.Player.DutyActionCharges, _dutyChargesScratch))
            return;

        ws.Execute(new PlayerInfo.OpDutyActions(active, new Dictionary<uint, ushort>(_dutyChargesScratch)));
    }

    private static bool DutyChargesEqual(Dictionary<uint, ushort> current, Dictionary<uint, ushort> next) {
        if (current.Count != next.Count)
            return false;
        foreach (var (id, charges) in next) {
            if (!current.TryGetValue(id, out var prev) || prev != charges)
                return false;
        }
        return true;
    }

    private static List<(int, Cooldown)> CalcCooldownDifference(ReadOnlySpan<Cooldown> values, ReadOnlySpan<Cooldown> reference) {
        var res = new List<(int, Cooldown)>();
        for (var i = 0; i < Math.Min(values.Length, reference.Length); i++) {
            if (values[i] != reference[i])
                res.Add((i, values[i]));
        }
        return res;
    }

    private static void UpdateTerritory(WorldState ws) {
        var territory = Svc.ClientState.TerritoryType;
        if (ws.TerritoryId != territory)
            ws.Execute(new WorldState.OpTerritory(territory));
    }

    private unsafe void UpdatePartyAndInstance(WorldState ws) {
        var inInstance = EventFramework.Instance()->GetInstanceContentDirector() != null;
        if (inInstance != ws.Party.InInstanceContent) {
            // snapshot who we queued with when we enter an instance, clear it when we leave
            ws.Execute(new PartyState.OpQueuedWith(inInstance ? ws.Party.ContentIds : []));
            ws.Execute(new PartyState.OpInInstanceContent(inInstance));
        }

        _partyScratch.Clear();
        var group = GroupManager.Instance()->MainGroup;
        for (var i = 0; i < group.MemberCount; i++) {
            var member = group.GetPartyMemberByIndex(i);
            if (member != null)
                _partyScratch.Add(member->ContentId);
        }

        if (PartyContentIdsEqual(ws.Party.ContentIds, _partyScratch))
            return;

        ws.Execute(new PartyState.OpMembers([.. _partyScratch]));
    }

    private static bool PartyContentIdsEqual(IReadOnlyList<ulong> current, List<ulong> next) {
        if (current.Count != next.Count)
            return false;
        for (var i = 0; i < next.Count; i++) {
            if (current[i] != next[i])
                return false;
        }
        return true;
    }

    private static unsafe void UpdateWeather(WorldState ws) {
        var wm = WeatherManager.Instance();
        if (wm == null)
            return;

        var currentModified = (uint)wm->GetCurrentWeather();

        // porting-note(api13): upstream reads the forecast off TerritoryType extension methods that
        // live in a newer ECommons than the one pinned here. The game exposes the same forecast
        // directly: GetWeatherForDaytime(territoryId, daytimeOffset) walks the deterministic weather
        // table, 0 being the period we are in. This is the same call the ported
        // Ices-Cosmic-Exploration weather panel uses for its forecast list.
        //
        // The offset parameter is a signed Int32 in CS 6966 (verified from the decoded signature),
        // so -1 is representable and is the natural way to ask for the preceding period - but
        // whether the implementation honours a negative offset is a RUNTIME question that metadata
        // cannot answer. Only the "prev" slot of the Weather condition depends on it; current and
        // next are unaffected either way.
        var territoryId = (ushort)ws.TerritoryId;
        var current = (uint)wm->GetWeatherForDaytime(territoryId, 0);
        var previous = (uint)wm->GetWeatherForDaytime(territoryId, -1);
        var next = (uint)wm->GetWeatherForDaytime(territoryId, 1);

        if (ws.CurrentModifiedWeatherId == currentModified && ws.CurrentWeatherId == current && ws.PreviousWeatherId == previous && ws.NextWeatherId == next)
            return;

        ws.Execute(new WorldState.OpWeather(currentModified, current, previous, next));
    }

    private static void UpdateBiteContext(WorldState ws, BiteContext biteContext) {
        var chumChanged = ws.Fishing.ChumActive != biteContext.ChumActive;
        var timeDelta = Math.Abs(ws.Fishing.BiteInfo.BiteTimeSeconds - biteContext.BiteTimeSeconds);
        if (!chumChanged && timeDelta < BiteTimeLogThreshold)
            return;
        ws.Execute(new FishingInfo.OpBiteContext(biteContext.BiteTimeSeconds, biteContext.ChumActive));
    }

    private static void UpdateIntuition(WorldState ws, BiteContext biteContext) {
        // on edge we want to report intuition as "gained/lost", then wait for extraoptions to poll it and set it as "active/inactive"
        var isActive = biteContext.IntuitionStatus == IntuitionStatus.Active;
        var prev = ws.Fishing.Intuition.Status;
        var wasActive = prev is IntuitionStatus.Active or IntuitionStatus.Gained;

        var nextStatus = isActive ? wasActive ? prev : IntuitionStatus.Gained : wasActive ? IntuitionStatus.Lost : prev;
        var next = new IntuitionInfo(nextStatus, isActive ? biteContext.IntuitionTimeRemaining : 0f);
        if (ws.Fishing.Intuition == next)
            return;
        ws.Execute(new FishingInfo.OpIntuition(next));
    }

    private static unsafe void UpdateSpearfishing(WorldState ws) {
        var windowOpen = false;
        var wariness = 0;
        var warinessMax = 0;
        AddonSpearFishing.FishInfo lane0 = default;
        AddonSpearFishing.FishInfo lane1 = default;
        AddonSpearFishing.FishInfo lane2 = default;

        // porting-note(api13): this tree's ECommons has no IGameGui.TryGetAddon<T> extension;
        // TryGetAddonByName is the idiom AutoHook already uses elsewhere (AetherialReduction).
        if (TryGetAddonByName<AddonSpearFishing>("SpearFishing", out var addon)
            && addon != null
            && addon->AtkUnitBase.WindowNode != null) {
            windowOpen = true;
            var gauge = addon->GaugeBar;
            if (gauge != null) {
                wariness = gauge->Values[0].ValueInt;
                warinessMax = gauge->MaxValue;
            }

            lane0 = addon->Fish[0];
            lane1 = addon->Fish[1];
            lane2 = addon->Fish[2];
        }

        var sf = ws.Spearfishing;
        if (sf.WindowOpen != windowOpen || sf.Wariness != wariness || sf.WarinessMax != warinessMax)
            ws.Execute(new SpearfishingInfo.OpHud(windowOpen, wariness, warinessMax));

        if (windowOpen && !sf.SessionActive)
            ws.Execute(new SpearfishingInfo.OpSessionActive(true));

        if (windowOpen) {
            var spot = ResolveCurrentSpearfishingSpot();
            if (!spot.IsEmpty && spot != sf.Spot)
                ws.Execute(new SpearfishingInfo.OpSpot(spot));
        }

        if (!FishInfoEquals(lane0, sf.Lane0) || !FishInfoEquals(lane1, sf.Lane1) || !FishInfoEquals(lane2, sf.Lane2))
            ws.Execute(new SpearfishingInfo.OpFishLanes(lane0, lane1, lane2));
    }

    private static bool FishInfoEquals(AddonSpearFishing.FishInfo a, AddonSpearFishing.FishInfo b)
        => a.Available == b.Available && a.InverseDirection == b.InverseDirection && a.GuaranteedLarge == b.GuaranteedLarge && a.Size == b.Size && a.Speed == b.Speed;

    private static SpearfishingSpotState ResolveCurrentSpearfishingSpot() {
        if (Svc.Targets.Target is not { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.GatheringPoint, BaseId: var pointId })
            return SpearfishingSpotState.Empty;

        if (!GameRes.SpearfishingSpotsByPointId.TryGetValue(pointId, out var spot))
            return new SpearfishingSpotState(pointId, 0, 0, false);

        return new SpearfishingSpotState(spot.GatheringPointId, spot.GatheringPointBaseId, spot.NotebookId, spot.IsShadowNode);
    }

    private void ProcessSpearfishingCatches(WorldState ws) {
        if (GameRes.SpearfishItemIds.Count == 0)
            return;

        _spearItemCountScratch.Clear();
        foreach (var itemId in GameRes.SpearfishItemIds) {
            var count = ws.Player.GetItemCount(itemId);
            if (count > 0)
                _spearItemCountScratch[itemId] = count;
        }

        if (!_spearItemCountsReady) {
            CommitSpearItemBaseline();
            return;
        }

        if (!ws.Spearfishing.SessionActive) {
            CommitSpearItemBaseline();
            return;
        }

        foreach (var (itemId, count) in _spearItemCountScratch) {
            var prev = _spearItemBaseline.GetValueOrDefault(itemId);
            if (count > prev) {
                var gained = count - prev;
                while (gained > 0) {
                    var chunk = (byte)Math.Min(gained, byte.MaxValue);
                    ws.Execute(new SpearfishingInfo.OpAddFishCaught(itemId, chunk));
                    gained -= chunk;
                }
            }
        }

        CommitSpearItemBaseline();
    }

    private readonly Dictionary<uint, int> _spearItemBaseline = [];

    private void CommitSpearItemBaseline() {
        _spearItemBaseline.Clear();
        foreach (var (itemId, count) in _spearItemCountScratch)
            _spearItemBaseline[itemId] = count;
        _spearItemCountsReady = true;
    }

    private static BiteContext CollectBiteContext(WorldState ws) {
        return new BiteContext {
            BiteTimeSeconds = ws.Fishing.BiteInfo.BiteTimeSeconds,
            ChumActive = ws.Player.HasStatus(IDs.Status.Chum),
            IntuitionStatus = ws.Player.HasStatus(IDs.Status.FishersIntuition) ? IntuitionStatus.Active : IntuitionStatus.NotActive,
            IntuitionTimeRemaining = ws.Player.GetStatusTime(IDs.Status.FishersIntuition),
            SpectralCurrentStatus = ws.Ocean.SpectralCurrentStatus,
            LastCaughtFishId = ws.Fishing.LastCatch?.FishId,
        };
    }

    private unsafe void UpdateOceanFishing(WorldState ws) {
        var ptr = EventFramework.Instance()->GetInstanceContentOceanFishing();
        if (ptr == null) {
            if (ws.Ocean.OceanFishing != OceanFishingState.Empty)
                ws.Execute(new OceanFishInfo.OpOceanFishing(null));
            return;
        }

        _fishDataScratch.Clear();
        foreach (var f in ptr->FirstZoneFishData)
            _fishDataScratch.Add(f);
        foreach (var f in ptr->SecondZoneFishData)
            _fishDataScratch.Add(f);
        foreach (var f in ptr->ThirdZoneFishData)
            _fishDataScratch.Add(f);

        var routeRow = Sheets.GetRow<IKDRoute>(ptr->CurrentRoute);
        var zoneIndex = (int)ptr->CurrentZone;
        var timeId = routeRow.Time[zoneIndex].RowId;
        var state = new OceanFishingState {
            SpectralCurrentActive = ptr->SpectralCurrentActive,
            CurrentRoute = ptr->CurrentRoute,
            TimeOfDay = (TimeOfDay)timeId,
            CurrentZone = ptr->CurrentZone,
            CurrentSpotId = routeRow.Spot[zoneIndex].RowId,
            CurrentTimeId = timeId,
            TimeLeftInZone = Math.Max(0f, EventFramework.Instance()->GetInstanceContentDirector()->ContentTimeLeft - ptr->TimeOffset),
            ZoneTimeMax = ptr->GetContentTimeMax(),
            Mission1 = new OceanMission(ptr->Mission1Type, ptr->Mission1Progress),
            Mission2 = new OceanMission(ptr->Mission2Type, ptr->Mission2Progress),
            Mission3 = new OceanMission(ptr->Mission3Type, ptr->Mission3Progress),
            PlayerCount = Svc.Objects.OfType<IPlayerCharacter>().Count(),
            FishData = [.. _fishDataScratch],
            Status = ptr->Status,
        };

        if (ws.Ocean.OceanFishing.SameAs(state))
            return;

        ws.Execute(new OceanFishInfo.OpOceanFishing(state));
    }

    private unsafe void UpdateFishingState(WorldState ws, BiteContext biteContext) {
        var state = FishingState.NotFishing;
        uint baitId = 0;
        uint? swimbaitId = null;
        var isMooching = false;
        PreviousCatchInfo previousCatch = default;
        var canFish = false;
        var changingPosition = false;
        FishingBaitFlags castFlags = 0;
        sbyte selectedSwimbait = 0;
        long moochExpire = 0;
        long catchExpire = 0;

        try {
            if (IsCosmicExplorationZone()) {
                // porting-note(api13): CS 6966's WKSManager has no State sub-struct - FishingBait
                // sits directly on the manager. Same field, one level shallower.
                if (WKSManager.Instance() is not null and var cosmic)
                    baitId = cosmic->FishingBait;
            }
            else
                baitId = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance()->FishingBait;

            var ef = EventFramework.Instance();
            // porting-note(api13): CS 6966 types EventHandlerModule.FishingEventHandler as the base
            // EventHandler*, not FishingEventHandler* (only CraftEventHandler kept its concrete
            // pointer type in this build). The pointed-at object IS the fishing handler, so cast.
            var handler = ef != null ? (FishingEventHandler*)ef->EventHandlerModule.FishingEventHandler : null;
            if (handler != null) {
                state = handler->State;
                selectedSwimbait = (sbyte)handler->CurrentSelectedSwimBait;
                // SwimBaitItemIds (a fixed array) does not exist in CS 6966; the three ids are
                // separate fields. Same data, indexed by hand.
                swimbaitId = selectedSwimbait switch {
                    0 => handler->SwimBaitId1,
                    1 => handler->SwimBaitId2,
                    2 => handler->SwimBaitId3,
                    _ => 0,
                };
                canFish = handler->CanFish;
            }

            // porting-note(api13): CS 6966's FishingEventHandler predates the mooch/expiration block
            // upstream reads (CurrentCastBaitFlags, Can*PreviousCatch, ChangingPosition,
            // MoochOpportunityExpirationTime, CatchActionExpirationTime are all ABSENT).
            //
            // The Can*PreviousCatch group is NOT approximated - the game enables exactly those
            // actions when the previous catch permits them, so ActionTypeAvailable is the same
            // predicate read from the other side. MoochAvailableCD is the only condition that
            // consumes this record, and it only reads the two mooch flags.
            //
            // The rest degrade (B1): ChangingPosition and both expiration timestamps have no
            // consumer in the condition engine - they are only stored and serialised into replays -
            // so they stay at their defaults rather than being invented.
            isMooching = Service.BaitManager?.IsMooching() ?? false;
            previousCatch = new PreviousCatchInfo(
                PlayerRes.ActionTypeAvailable(IDs.Actions.Mooch),
                PlayerRes.ActionTypeAvailable(IDs.Actions.Mooch2),
                PlayerRes.ActionTypeAvailable(IDs.Actions.Release),
                PlayerRes.ActionTypeAvailable(IDs.Actions.IdenticalCast),
                PlayerRes.ActionTypeAvailable(IDs.Actions.SurfaceSlap));
            castFlags = (isMooching ? FishingBaitFlags.Mooch : FishingBaitFlags.None)
                      | (swimbaitId != 0 ? FishingBaitFlags.Swimbait : FishingBaitFlags.None);
        }
        catch { }

        var baitMoochId = ComputeCurrentBaitMoochId(baitId, swimbaitId, isMooching, biteContext);
        var bait = new BaitInfo(baitId, swimbaitId, baitMoochId, isMooching);

        if (ws.Fishing.FishingState != state || ws.Fishing.BaitInfo != bait)
            ws.Execute(new FishingInfo.OpFishingState(state, bait));

        var handlerState = new FishingInfo.OpFishingHandlerState(
            previousCatch, canFish, changingPosition, castFlags, selectedSwimbait, moochExpire, catchExpire);
        var f = ws.Fishing;
        if (f.PreviousCatch != previousCatch || f.CanFish != canFish || f.ChangingPosition != changingPosition ||
            f.CurrentCastBaitFlags != castFlags || f.CurrentSelectedSwimbait != selectedSwimbait ||
            f.MoochOpportunityExpirationTime != moochExpire || f.CatchActionExpirationTime != catchExpire)
            ws.Execute(handlerState);
    }

    private unsafe void UpdateSwimbaitIds(WorldState ws) {
        _swimbaitScratch.Clear();
        try {
            if (EventFramework.Instance() is not null and var ef && ef->EventHandlerModule.FishingEventHandler is not null and var eh) {
                var handler = (FishingEventHandler*)eh;
                _swimbaitScratch.Add(handler->SwimBaitId1);
                _swimbaitScratch.Add(handler->SwimBaitId2);
                _swimbaitScratch.Add(handler->SwimBaitId3);
            }
        }
        catch { }

        if (SwimbaitIdsEqual(ws.Fishing.SwimbaitIds, _swimbaitScratch))
            return;

        ws.Execute(new FishingInfo.OpSwimbaitIds([.. _swimbaitScratch]));
    }

    private static bool SwimbaitIdsEqual(List<uint> current, List<uint> next) {
        if (current.Count != next.Count)
            return false;
        for (var i = 0; i < current.Count; i++) {
            if (current[i] != next[i])
                return false;
        }
        return true;
    }

    private unsafe void UpdatePotCooldown(WorldState ws) {
        var off = false;
        var am = ActionManager.Instance();
        if (am != null) {
            var recast = am->GetRecastGroupDetail(68);
            if (recast != null)
                off = recast->Total - recast->Elapsed <= 0;
        }

        if (ws.Player.IsPotOffCooldown == off)
            return;

        ws.Execute(new PlayerInfo.OpPotCooldown(off));
    }

    private static unsafe WKSInfo.OpState CollectWKSInfo() {
        ushort devGrade = 0;
        ushort currentFateControlRowId = 0;
        ushort currentFateId = 0;
        ushort currentMissionUnitRowId = 0;
        uint currentScore = 0;
        var currentRank = WksMissionRank.None;
        ushort collectedTotal = 0;
        byte collectedIndividual = 0;

        try {
            if (IsCosmicExplorationZone() && WKSManager.Instance() is not null and var wks) {
                // porting-note(api13): CS 6966's WKSManager has no State sub-struct and no
                // CurrentMission block - the four ids below sit directly on the manager, so those
                // are exact. The per-mission score/rank/collected counters have no representation
                // in this build at all (B1): they feed only the cosmic-mission conditions, which
                // no preset in the measured wiki corpus uses, so they stay at their defaults
                // rather than being fabricated from _scores.
                devGrade = wks->DevGrade;
                currentFateControlRowId = wks->CurrentFateControlRowId;
                currentFateId = wks->CurrentFateId;
                currentMissionUnitRowId = wks->CurrentMissionUnitRowId;
            }
        }
        catch { }

        return new WKSInfo.OpState(devGrade, currentFateControlRowId, currentFateId, currentMissionUnitRowId, currentScore, currentRank, collectedTotal, collectedIndividual);
    }

    private static void UpdateWKS(WorldState ws) {
        var next = CollectWKSInfo();
        var w = ws.WKS;
        if (w.DevGrade == next.DevGrade && w.CurrentFateControlRowId == next.CurrentFateControlRowId &&
            w.CurrentFateId == next.CurrentFateId && w.CurrentMissionUnitRowId == next.CurrentMissionUnitRowId &&
            w.CurrentScore == next.CurrentScore && w.CurrentRank == next.CurrentRank &&
            w.CollectedTotal == next.CollectedTotal && w.CollectedIndividual == next.CollectedIndividual)
            return;
        ws.Execute(next);
    }

    private unsafe bool UseActionDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted) {
        try {
            if (actionType == ActionType.Action && Service.Configuration.PluginEnabled && Service.WorldState.ActionAvailable(actionId, actionType))
                Service.WorldState.Execute(new FishingInfo.OpPlayerUsedAction(new UsedAction(actionId, actionType)));
        }
        catch (Exception e) {
            Service.PrintDebug($"[WorldStateUpdater] UseAction: {e.Message}");
        }
        return _useActionHook!.Original(thisPtr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    /// <summary>
    /// Stands in for upstream's AgentCatch.UpdateCatch hook, a type CS 6966 does not have.
    /// Fed from UpdateCatchDetour above, whose signature hook carries the identical parameter list
    /// and is already runtime-verified on this generation.
    /// </summary>
    private void UpdateCatchDetour(IntPtr module, uint fishId, bool large, ushort size, byte amount,
        byte level, byte stars, byte oceanStars, bool isMoochable, bool isFirstTimeCatch, byte a11, byte a12) {
        _updateCatchHook!.Original(module, fishId, large, size, amount, level, stars, oceanStars,
            isMoochable, isFirstTimeCatch, a11, a12);
        NotifyCatch(fishId, large, size, amount, level, stars, oceanStars, isMoochable, isFirstTimeCatch);
    }

    public void NotifyCatch(uint itemId, bool isLarge, ushort size, byte amount, byte level, byte stars, byte oceanStars, bool isMoochable, bool isFirstTimeCatch) {
        if (ItemUtil.GetBaseId(itemId) is { ItemId: > 0 and var id }) {
            Service.PrintDebug($"Caught fish: {id}, amount: {amount}, large: {isLarge}, size: {size}, level: {level}, stars: {stars}, oceanStars: {oceanStars}, moochable: {isMoochable}, firstTimeCatch: {isFirstTimeCatch}");
            Service.WorldState.Execute(new FishingInfo.OpSetLastCatch(new CatchInfo(id, amount, isLarge, size, level, stars, oceanStars, isMoochable, isFirstTimeCatch)));
        }
        Service.WorldState.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.FishCaught));
    }

    private unsafe void ReceiveAchievementProgressDetour(AchievementStruct* thisPtr, uint id, uint current, uint max) {
        _receiveAchievementProgressHook!.Original(thisPtr, id, current, max);
        Service.WorldState.Execute(new WorldState.OpAchievementProgress(id, current, max));
    }

    /// <summary>
    /// Stands in for upstream's PlayAnimation hook, whose vtable slot CS 6966 does not expose.
    /// Fed from Update() on the transition into FishingState.Bite, with the tug read from the
    /// static address this tree already ships (and has runtime-verified).
    /// </summary>
    public void NotifyBite(BiteType bite) {
        var tugType = (FishingHookStrength)bite;
        if (tugType is FishingHookStrength.Weak or FishingHookStrength.Strong or FishingHookStrength.Legendary) {
            Service.WorldState.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.FishBit));
            Service.WorldState.Execute(new FishingInfo.OpTugType(tugType));
        }
        else {
            Service.WorldState.Execute(new FishingInfo.OpTugType(0));
        }
    }


    private static readonly HashSet<uint> FishIdSet = [];

    public static uint ComputeCurrentBaitMoochId(uint currentId, uint? swimbaitId, bool isMooching, BiteContext biteContext) {
        if (swimbaitId.HasValue && swimbaitId.Value != 0)
            return swimbaitId.Value;
        if (FishIdSet.Count == 0) {
            foreach (var fish in GameRes.Fishes)
                FishIdSet.Add((uint)fish.Id);
        }
        if (FishIdSet.Contains(currentId))
            return currentId;
        if (isMooching && biteContext.LastCaughtFishId is { } lastId && lastId > 0 && FishIdSet.Contains(lastId))
            return lastId;
        return currentId;
    }

    private static unsafe (PlayerInfo.OpItemCounts Counts, PlayerInfo.OpInventoryStats Stats) CollectInventory() {
        var dict = new Dictionary<uint, int>();
        var freeSlots = 0;
        var reduceableFish = 0;
        try {
            var inv = InventoryManager.Instance();
            if (inv != null) {
                for (var i = 0; i < 4; i++) {
                    var container = inv->GetInventoryContainer((InventoryType)i);
                    if (container == null) continue;
                    for (var k = 0; k < container->Size; k++) {
                        var slot = container->GetInventorySlot(k);
                        if (slot == null || slot->ItemId == 0) continue;
                        var kind = slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ? ItemKind.Hq : slot->Flags.HasFlag(InventoryItem.ItemFlags.Collectable) ? ItemKind.Collectible : ItemKind.Normal;
                        var id = ItemUtil.GetRawId(slot->ItemId, kind);
                        dict[id] = dict.GetValueOrDefault(id, 0) + slot->Quantity;
                    }
                }

                // porting-note(api13): CS 6966 has neither InventoryType.Bags nor
                // InventoryManager.GetInventoryItems. The four player bags are InventoryType
                // Inventory1..Inventory4 (= 0..3), walked the same way as the loop above.
                for (var i = 0; i < 4; i++) {
                    var container = inv->GetInventoryContainer((InventoryType)i);
                    if (container == null) continue;
                    for (var k = 0; k < container->Size; k++) {
                        var slot = container->GetInventorySlot(k);
                        if (slot == null || slot->ItemId == 0)
                            freeSlots++;
                        else if (IsReduceableFish(slot))
                            reduceableFish++;
                    }
                }
            }
        }
        catch { }

        try {
            if (IsCosmicExplorationZone()) {
                var cosmopouch = ContentInventoryManager.Instance()->WKSInventoryProvider.Cosmopouch1;
                foreach (ref readonly var item in cosmopouch.WKSItems) {
                    if (item.WKSItemId == 0)
                        continue;
                    dict[item.WKSItemId] = item.WKSItemQuantity;
                }
            }
        }
        catch { }

        return (new PlayerInfo.OpItemCounts(dict), new PlayerInfo.OpInventoryStats(freeSlots, reduceableFish));
    }

    private static bool IsCosmicExplorationZone()
        => Player.Territory is { Value.TerritoryIntendedUse.RowId: 60 };

    private static unsafe bool IsReduceableFish(Pointer<InventoryItem> item)
        => item.Value->Flags == InventoryItem.ItemFlags.Collectable && TryGetRow<Item>(item.Value->ItemId, out var row) && row.AetherialReduce > 0;
}
