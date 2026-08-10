using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Excel.Sheets;

namespace AutoHook.Fishing;

public partial class FishingManager {
    private void AnimationCancel() {
        if (GetAutoCastCfg().RecastAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Collect);

        if (Ws.HasStatus(IDs.Status.Salvage) && GetAutoCastCfg().ChumAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Salvage);
    }

    // porting-note(api13): upstream splits this across two api15-only chat APIs -
    // IChatGui.LogMessage (ILogMessage, which hands over LogMessageId directly) and an
    // IHandleableChatMessage overload of ChatMessage. Neither exists on api13, which only has the
    // classic ChatMessage delegate. This build's own pre-existing handler already solved the same
    // problem from the other side and is runtime-verified on TC (session 15): match the message text
    // back to its LogMessage row and read the RowId. Both upstream branches are preserved, including
    // upstream's newer LureTarget switch; only the way the id is obtained differs.
    //
    // The two chat-type constants are this build's measured TC values, kept rather than upstream's
    // XivChatType.Gathering, because they are the ones known to fire on this client.
    private const XivChatType FishingMessage = (XivChatType)2243;
    private const XivChatType SystemAlert = (XivChatType)2115;

    private void OnMessageDelegate(XivChatType type, int timeStamp, ref SeString sender, ref SeString messageSe,
        ref bool isHandled) {
        try {
            var text = messageSe.TextValue;

            if (type is FishingMessage) {
                var lureTarget = GetHookCfg().GetHookset().CastLures.LureTarget;

                var isSpecialLure = GameRes.LureFishes.FirstOrDefault(f => f.LureMessage == text) != null;
                if (lureTarget is LureTarget.Any or LureTarget.Special && isSpecialLure) {
                    Ws.Execute(new FishingInfo.OpSetLureSuccess(true));
                    return;
                }

                var isGenericLure = FindRow<LogMessage>(x => x.Text.ToString() == text)
                    is { RowId: LogMessageIds.AmbLureSuccess or LogMessageIds.ModLureSuccess };
                if (lureTarget is LureTarget.Any or LureTarget.NotSpecial && isGenericLure)
                    Ws.Execute(new FishingInfo.OpSetLureSuccess(true));
            }
            else if (type is SystemAlert) {
                if (FindRow<LogMessage>(x => x.Text.ToString() == text) is { RowId: LogMessageIds.CantFish })
                    Service.Status = UIStrings.CantFishHere;
            }
        }
        catch (Exception e) {
            Svc.Log.Error(e.Message);
        }
    }

    // This is my stupid way of handling the counter for stop/quit fishing and bait/preset swap
    public static class FishingHelper {
        public static Dictionary<Guid, int> FishCount = [];
        public static List<Guid> FishPresetSwapped = [];
        public static List<Guid> FishBaitSwapped = [];

        public static List<Guid> ToBeRemoved = [];

        public static void AddFishCount(Guid guid) {
            FishCount.TryAdd(guid, 0);
            FishCount[guid]++;

            GetFishCount(guid);
        }

        public static void AddBaitSwap(Guid guid) {
            if (!FishBaitSwapped.Contains(guid))
                FishBaitSwapped.Add(guid);
        }

        public static void AddPresetSwap(Guid guid) {
            if (!FishPresetSwapped.Contains(guid))
                FishPresetSwapped.Add(guid);
        }

        public static void RemovePresetSwap(Guid guid) {
            if (SwappedPreset(guid))
                FishPresetSwapped.Remove(guid);
        }

        public static int GetFishCount(Guid guid) {
            return !FishCount.TryGetValue(guid, out var value) ? 0 : value;
        }

        public static bool SwappedBait(Guid guid) {
            return FishBaitSwapped.Any(g => g == guid);
        }

        public static bool SwappedPreset(Guid guid) {
            return FishPresetSwapped.Any(g => g == guid);
        }

        public static void RemoveId(Guid guid) {
            FishCount.Remove(guid);

            if (SwappedPreset(guid))
                FishPresetSwapped.Remove(guid);

            if (SwappedBait(guid))
                FishBaitSwapped.Remove(guid);
        }

        public static void RemoveGuidQueue() {
            foreach (var guid in ToBeRemoved) {
                FishCount.Remove(guid);

                if (SwappedPreset(guid))
                    FishPresetSwapped.Remove(guid);

                if (SwappedBait(guid))
                    FishBaitSwapped.Remove(guid);
            }

            ToBeRemoved.Clear();
        }

        public static void Reset() {
            FishCount = [];
            FishPresetSwapped = [];
            FishBaitSwapped = [];
        }
    }
}
