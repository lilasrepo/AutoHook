using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Text.RegularExpressions;

namespace clib.Ui.Table;

// https://github.com/Ottermandias/OtterGui/blob/79771ee5f3d463f02c63bebbedaa0aff49e59718/Table/ColumnString.cs#L6
public class ColumnString<TItem>(Func<TItem, string> toText) : Column<TItem> {
    public string FilterValue = string.Empty;
    private Regex? _filterRegex;

    public override bool DrawFilter() {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0);
        ImGui.SetNextItemWidth(-Table<TItem>.ArrowWidth * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        var tmp = FilterValue;
        if (!ImGui.InputTextWithHint(FilterLabel, Label, ref tmp, 256) || tmp == FilterValue)
            return false;

        FilterValue = tmp;
        try {
            _filterRegex = FilterValue.IsEmpty ? null : new Regex(FilterValue, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch {
            _filterRegex = null;
        }

        return true;
    }

    public override bool FilterFunc(TItem item) {
        if (FilterValue.Length == 0)
            return true;

        var text = toText(item);
        return _filterRegex?.IsMatch(text) ?? text.Contains(FilterValue, StringComparison.OrdinalIgnoreCase);
    }

    public override void DrawColumn(TItem item) => ImGui.TextUnformatted(toText(item));
}
