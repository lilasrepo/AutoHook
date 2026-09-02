using Dalamud.Game;
using Dalamud.Plugin;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Reflection;

namespace clib.Extensions;

public static class IDalamudPluginInterfaceExtensions {
    extension(IDalamudPluginInterface pi) {
        /// <summary>
        /// This is the commit number, not hash
        /// </summary>
        public uint ClientStructsVersion => get_CsVersion(pi).Value;
        internal Lazy<uint> CsVersion => new(() => (uint?)typeof(FFXIVClientStructs.ThisAssembly).Assembly.GetName().Version?.Build ?? 0U);

        public bool IsPluginLoaded(string internalName) => pi.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded);

        // https://github.com/Haselnussbomber/HaselCommon/blob/70a1689272b0e4bd80bb957abe9ec7a2f5bf4549/HaselCommon/Extensions/Dalamud/IDalamudPluginInterfaceExtensions.cs#L10
        public void InitCustomClientStructs() {
            var sigScanner = pi.GetRequiredService<ISigScanner>();
            var dataManager = pi.GetRequiredService<IDataManager>();
            FFXIVClientStructs.Interop.Generated.Addresses.Register();
            InteropGenerator.Runtime.Resolver.GetInstance.Setup(sigScanner.SearchBase, dataManager.GameData.Repositories["ffxiv"].Version, new(Path.Join(pi.ConfigDirectory.FullName, "SigCache.json")));
            InteropGenerator.Runtime.Resolver.GetInstance.Resolve();
        }

        public object? GetService(string serviceName)
            => pi.GetType().Assembly.GetType("Dalamud.Service`1", true)?.MakeGenericType(pi.GetType().Assembly.GetType(serviceName, true)!).GetMethod("Get")?.Invoke(null, BindingFlags.Default, null, [], null);

        public void ToggleDtr() {
            var config = pi.GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
            if (config?.GetFieldOrProperty<List<string>>("DtrIgnore") is { } ignoreList) {
                if (!ignoreList.Contains(pi.InternalName))
                    ignoreList.Remove(pi.InternalName);
                else
                    ignoreList.Add(pi.InternalName);
                config.CallMethod("QueueSave", []);
            }
        }
    }
}
