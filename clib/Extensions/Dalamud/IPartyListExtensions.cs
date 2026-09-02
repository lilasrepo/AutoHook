using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace clib.Extensions;

public static class IPartyListExtensions {
    extension(IPartyList party) {
        public bool AllTargetable() => party.All(p => p.GameObject?.IsTargetable ?? false);
        public unsafe bool DisbandParty() => InfoProxyPartyMember.Instance()->DisbandParty();
        public unsafe bool LeaveParty() => InfoProxyPartyMember.Instance()->LeaveParty();
    }
}
