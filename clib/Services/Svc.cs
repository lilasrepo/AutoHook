using AllaganLib.GameSheets.Service;
using clib.Configuration;
using Dalamud.Configuration;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace clib.Services;

public class Svc {
    [PluginService] public static IDalamudPluginInterface Interface { get; private set; } = null!;

    // porting-note(api13): the Items service (armoire/dresser/mirage tracking) is dead weight here
    // - see clib.csproj's Compile Remove group for why ArmoireService/DresserService/ItemService
    // are cut, and note AutoHook's own upstream CLibMain.Init call never passes CLibModule.Items
    // anyway (verified against JP/AutoHook HEAD).
    public static Automation Automation { get; private set; } = null!;
    public static SheetManager SheetManager { get; private set; } = null!;

    internal static NavmeshIPC Navmesh { get; private set; } = null!;
    internal static WindowSystem Windows { get; private set; } = null!;

    private static readonly ConcurrentDictionary<Type, object> Singletons = new();
    private static readonly Dictionary<Type, ConstructorInfo> Unconstructed = [];

    public static void Register<T>() where T : class, new()
        => Register(() => new T());

    public static void Register<T>(Func<T> singleton) where T : class {
        ArgumentNullException.ThrowIfNull(singleton);
        var key = typeof(T);
        var instance = singleton();
        if (!Singletons.TryAdd(key, instance))
            throw new InvalidOperationException($"[{nameof(Svc)}] {key.FullName} is already registered.");
    }

    public static T Get<T>() where T : class {
        if (!Singletons.TryGetValue(typeof(T), out var instance))
            throw new InvalidOperationException($"[{nameof(Svc)}] {typeof(T).FullName} has not been registered.");
        ConstructIfNeeded(typeof(T));
        return (T)instance;
    }

    public static IEnumerable<T> GetServices<T>() where T : class
        => Singletons.Values.OfType<T>();

    internal static void Init(IDalamudPluginInterface pi, object pluginInstance, CLibModule modules) {
        pi.Create<Svc>();
        Navmesh = new();
        Windows = new();

        pi.UiBuilder.Draw += Windows.Draw;

        if (modules.HasFlag(CLibModule.SheetManager))
            // NOTE(api13-probe): AllaganLib.GameSheets 1.3.12 (net9 pin; HEAD's 2.2.6 is net10-only)
            // has a 2-arg SheetManager ctor with no IDalamudPluginInterface parameter.
            SheetManager = new(IDataManager.Get().GameData, new());
        if (modules.HasFlag(CLibModule.Automation))
            Automation = new();

        RegisterPluginServices(pluginInstance.GetType().Assembly);
        RegisterPluginCommands();
    }

    internal static async ValueTask DisposeAsync() {
        Interface.UiBuilder.Draw -= Windows.Draw;

        await DisposeObjectAsync(Windows).ConfigureAwait(false);
        await DisposeObjectAsync(Automation).ConfigureAwait(false);
        await DisposeObjectAsync(SheetManager).ConfigureAwait(false);

        if (Singletons.TryRemove(typeof(PluginCommandHost), out var commandHost))
            await DisposeObjectAsync(commandHost).ConfigureAwait(false);

        foreach (var s in Singletons.Values) {
            try {
                await DisposeObjectAsync(s).ConfigureAwait(false);
            }
            catch {
                IPluginLog.Get().Error($"[{nameof(Svc)}] Failed disposal of {s.GetType().FullName}");
            }
        }

        Unconstructed.Clear();
        Singletons.Clear();
    }

    internal static void Dispose()
        => DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    private static ValueTask DisposeObjectAsync(object? obj) {
        if (obj is null) return ValueTask.CompletedTask;
        if (obj is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();
        if (obj is IDisposable disposable) {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }
        return ValueTask.CompletedTask;
    }

    private static void RegisterPluginServices(Assembly assembly) {
        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPluginService).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var type in types)
            RegisterPluginService(type, assembly, allowConfigFile: typeof(IPluginConfiguration).IsAssignableFrom(type));

        foreach (var type in Unconstructed.Keys.ToArray())
            ConstructIfNeeded(type);
    }

    private static void RegisterPluginService(Type type, Assembly assembly, bool allowConfigFile) {
        if (allowConfigFile && TryLoadPluginConfig(type, assembly, out var loaded)) {
            AddSingleton(type, loaded);
            return;
        }

        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null)
            ?? throw new InvalidOperationException($"[{nameof(Svc)}] {type.FullName} requires a public parameterless constructor.");

        AddSingleton(type, RuntimeHelpers.GetUninitializedObject(type));
        Unconstructed[type] = ctor;
    }

    private static void ConstructIfNeeded(Type type) {
        if (!Unconstructed.Remove(type, out var ctor))
            return;
        ctor.Invoke(Singletons[type], null);
    }

    private static void AddSingleton(Type type, object instance) {
        if (!Singletons.TryAdd(type, instance))
            throw new InvalidOperationException($"[{nameof(Svc)}] {type.FullName} is already registered.");
    }

    // need this because GetPluginConfig() wouldn't work from clib
    private static bool TryLoadPluginConfig(Type type, Assembly assembly, out object loaded) {
        loaded = null!;
        if (Interface.ConfigFile is not { Exists: true } file)
            return false;
        if (JsonConvert.DeserializeObject(File.ReadAllText(file.FullName), type) is not IPluginConfiguration config)
            return false;

        if (ConfigHelper.RunMigrationChain(config, assembly, out var final))
            SavePluginConfig(final);

        loaded = final;
        return true;
    }

    private static void SavePluginConfig(object config) {
        if (Interface.ConfigFile is not { Directory.Exists: true } file)
            return;

        try {
            File.WriteAllText(file.FullName, JsonConvert.SerializeObject(config, Formatting.Indented));
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{nameof(Svc)}] Failed to save migrated config");
        }
    }

    private static void RegisterPluginCommands() {
        var commandSets = Singletons.Values.OfType<IPluginCommands>().ToList();
        if (commandSets.Count == 0)
            return;

        if (!Singletons.TryAdd(typeof(PluginCommandHost), new PluginCommandHost(commandSets)))
            throw new InvalidOperationException($"[{nameof(Svc)}] {nameof(PluginCommandHost)} is already registered.");
    }
}

internal static class LogExtensions {
    public static void Print(this IPluginLog log, string message) => log.Debug($"[{nameof(clib)}] {message}");
    public static void PrintWarning(this IPluginLog log, string message) => log.Warning($"[{nameof(clib)}] {message}");
    public static void PrintError(this IPluginLog log, string message) => log.Error($"[{nameof(clib)}] {message}");
}
