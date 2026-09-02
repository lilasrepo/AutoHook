using FFXIVClientStructs.FFXIV.Component.GUI;

namespace clib.Extensions;

public static unsafe class AtkComponentNodeExtensions {
    extension(ref AtkComponentNode node) {
        public AtkUnitBase* OwnerAddon {
            get {
                fixed (AtkComponentNode* ptr = &node)
                    return ptr == null ? null : ((AtkResNode*)ptr)->OwnerAddon;
            }
        }
    }
}
