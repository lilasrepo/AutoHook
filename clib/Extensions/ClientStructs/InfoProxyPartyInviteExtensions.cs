using clib.Services;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace clib.Extensions;

public static unsafe class InfoProxyPartyInviteExtensions {
    public enum FailedInviteReason {
        None,
        GroupFull,
        NotPartyLead,
        TargetInParty,
    }

    extension(InfoProxyPartyInvite proxy) {
        public static bool CanInviteToParty(ulong contentId, [NotNullWhen(false)] out FailedInviteReason? reason) {
            if (GroupManager.Instance()->GetGroup()->MemberCount >= 8) {
                reason = FailedInviteReason.GroupFull;
                return false;
            }
            if (IPlayerState.Get() is { InParty: true, IsPartyLeader: false }) {
                reason = FailedInviteReason.NotPartyLead;
                return false;
            }
            if (IPartyList.Get().Any(p => (ulong)p.ContentId == contentId)) {
                reason = FailedInviteReason.TargetInParty;
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// Check <see cref="CanInviteToParty(ulong, out string?) first."/>
        /// </summary>
        public static bool Invite(ulong contentId, string playerName, ushort worldId) {
            if (InfoProxyCrossRealm.IsCrossRealmParty()) {
                // not in CS yet
                IPluginLog.Get().Print($"Unable to invite to cross-realm party");
                return false;
            }
            else if (ICondition.Get()[ConditionFlag.BoundByDuty56]) {
                IPluginLog.Get().Print($"Inviting to instanced party");
                return InfoProxyPartyInvite.Instance()->InviteToPartyInInstanceByContentId(contentId);
            }
            else {
                IPluginLog.Get().Print($"Inviting to local party");
                fixed (byte* namePtr = ToTerminatedBytes(playerName))
                    return InfoProxyPartyInvite.Instance()->InviteToParty(contentId, namePtr, worldId);
            }
        }
    }

    private static byte[] ToTerminatedBytes(string s) {
        var utf8 = Encoding.UTF8;
        var bytes = new byte[utf8.GetByteCount(s) + 1];
        utf8.GetBytes(s, 0, s.Length, bytes, 0);
        bytes[^1] = 0;
        return bytes;
    }
}
