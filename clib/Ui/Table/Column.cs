using Dalamud.Bindings.ImGui;

namespace clib.Ui.Table;

// https://github.com/Ottermandias/OtterGui/blob/79771ee5f3d463f02c63bebbedaa0aff49e59718/Table/Column.cs#L5
public class Column<TItem> {
    public string Label = string.Empty;
    public ImGuiTableColumnFlags Flags = ImGuiTableColumnFlags.None;
    public float Width = -1f;

    public string FilterLabel => $"##{Label}Filter";

    public virtual bool DrawFilter() {
        ImGui.TextUnformatted(Label);
        return false;
    }

    public virtual bool FilterFunc(TItem item) => true;

    public virtual void DrawColumn(TItem item) { }
}
