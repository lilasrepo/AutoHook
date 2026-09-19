using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace clib.Extensions;

public static unsafe class AddonMateriaAttachDialog {
    extension(AddonMateriaAttachDialog) {
        public static void Meld() {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("MateriaAttachDialog");
            if (AtkUnitBaseExtensions.IsReadyOnTC(addon)) {
                var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
                var data = new AtkEventData();
                addon->ReceiveEvent(AtkEventType.ButtonClick, 0, &evt, &data);
            }
        }
    }
}
