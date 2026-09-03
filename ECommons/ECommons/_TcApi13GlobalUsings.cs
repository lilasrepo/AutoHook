// porting-note(api13): TC-only file, not from upstream ECommons.
//
// FFXIVClientStructs 6966 (the generation shipped with TC_ok/_dalamud_api13) still names the
// AtkValue discriminator enum `FFXIVClientStructs.FFXIV.Component.GUI.ValueType`. Upstream CS
// later renamed it to `AtkValueType`, and ECommons 3.2.x writes the new name in 8 files.
//
// A global alias fixes all of them without touching a single upstream file, so an upstream
// refresh cannot silently drop the fix (an edited file would be overwritten; this one is not).
// Delete this file if the TC reference DLL set ever ships a real `AtkValueType`.
global using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;
