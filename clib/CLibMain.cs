using clib.Services;
using Dalamud.Plugin;
using System.Threading.Tasks;

namespace clib;

public static class CLibMain {
    public static string Name { get; private set; } = null!;

    /// <summary>
    /// Function to initialise clib services. Must call <see cref="Dispose"/> or <see cref="DisposeAsync"/> when you dispose of your plugin.
    /// </summary>
    public static void Init(IDalamudPluginInterface pi, object instance, CLibModule modules = CLibModule.None) {
        if (instance is not IDalamudPlugin)
            // TODO(api13): upstream also accepts IAsyncDalamudPlugin, which does not exist at this API level.
            throw new InvalidOperationException($"Invalid plugin instance. Must be of type {nameof(IDalamudPlugin)}");
        Svc.Init(pi, instance, modules);
        Name = pi.Manifest.Name;
    }

    public static ValueTask DisposeAsync() => Svc.DisposeAsync();
    public static void Dispose() => Svc.Dispose();
}
