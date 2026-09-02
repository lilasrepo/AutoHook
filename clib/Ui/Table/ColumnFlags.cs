using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace clib.Ui.Table;

// https://github.com/Ottermandias/OtterGui/blob/79771ee5f3d463f02c63bebbedaa0aff49e59718/Table/ColumnFlags.cs#L9
public class ColumnFlags<TEnum, TItem> : Column<TItem> where TEnum : struct, Enum {
    public TEnum AllFlags;
    public TEnum FilterValue;
    public Func<TItem, TEnum> GetFlags = _ => default;
    public Func<TEnum, string>? FormatName;
    public Func<TItem, string>? DisplayText;

    private ImGuiComboFlags ComboFlags => ImGuiComboFlags.NoArrowButton;

    public override bool DrawFilter() {
        using var id = ImRaii.PushId(FilterLabel);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0);
        ImGui.SetNextItemWidth(-Table<TItem>.ArrowWidth * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);

        var all = FilterValue.HasFlag(AllFlags);
        using var color = ImRaii.PushColor(ImGuiCol.FrameBg, 0x803030A0, !all);
        using var combo = ImRaii.Combo(string.Empty, Label, ComboFlags);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
            FilterValue = AllFlags;
            return true;
        }

        if (!all && ImGui.IsItemHovered())
            ImGui.SetTooltip("Right-click to clear filters.");

        if (!combo)
            return false;

        color.Pop();

        var changed = false;
        var enableAll = all;
        if (ImGui.Checkbox("Enable All", ref enableAll)) {
            FilterValue = enableAll ? AllFlags : default;
            changed = true;
        }

        using var indent = ImRaii.PushIndent(10f);
        foreach (var value in Enum.GetValues<TEnum>()) {
            if (value.Equals(AllFlags))
                continue;

            var enabled = FilterValue.HasFlag(value);
            var label = FormatName?.Invoke(value) ?? value.ToString() ?? string.Empty;
            if (!ImGui.Checkbox(label, ref enabled))
                continue;

            FilterValue = enabled ? FlagUnion(FilterValue, value) : FlagRemove(FilterValue, value);
            changed = true;
        }

        return changed;
    }

    public override bool FilterFunc(TItem item)
        => FilterValue.HasFlag(AllFlags) || FilterValue.HasFlag(GetFlags(item));

    public override void DrawColumn(TItem item) {
        if (DisplayText is { } display) {
            ImGui.TextUnformatted(display(item));
            return;
        }

        var flags = GetFlags(item);
        ImGui.TextUnformatted(FormatName?.Invoke(flags) ?? flags.ToString() ?? string.Empty);
    }

    private static TEnum FlagUnion(TEnum left, TEnum right)
        => (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToUInt64(left) | Convert.ToUInt64(right));

    private static TEnum FlagRemove(TEnum left, TEnum right)
        => (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToUInt64(left) & ~Convert.ToUInt64(right));
}
