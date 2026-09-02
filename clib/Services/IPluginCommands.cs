namespace clib.Services;

public interface IPluginCommands : IPluginService {
    string[] Commands { get; }
    string HelpMessage { get; }
    CommandNode<object> Root { get; }
}
