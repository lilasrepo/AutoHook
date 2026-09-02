using Dalamud.Game.Command;

namespace clib.Services;

/// <summary>
/// Registers <see cref="IPluginCommands"/> with Dalamud and prints router results to chat.
/// Created by <see cref="Svc"/> after plugin services are constructed.
/// </summary>
internal sealed class PluginCommandHost : IDisposable {
    private readonly List<string> _registered = [];

    public PluginCommandHost(IEnumerable<IPluginCommands> commandSets) {
        foreach (var set in commandSets) {
            if (set.Commands is not { Length: > 0 })
                throw new InvalidOperationException($"[{nameof(PluginCommandHost)}] {set.GetType().FullName} has no commands.");

            var router = new CommandRouter<object>(set.Root);
            var rootLabel = set.Commands[0];

            void OnCommand(string command, string args) {
                var result = router.Execute(args, null!, rootLabel);
                if (!result.Success) {
                    if (result.Error is not null)
                        IChatGui.Get().PrintError(result.Error);
                    if (result.Usage is not null)
                        IChatGui.Get().Print(result.Usage);
                    return;
                }

                if (result.Help is not null)
                    IChatGui.Get().Print(result.Help);
            }

            foreach (var alias in set.Commands) {
                ICommandManager.Get().AddHandler(alias, new CommandInfo(OnCommand) { HelpMessage = set.HelpMessage });
                _registered.Add(alias);
            }
        }
    }

    public void Dispose() {
        foreach (var alias in _registered)
            ICommandManager.Get().RemoveHandler(alias);
        _registered.Clear();
    }
}
