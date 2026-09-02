using Dalamud.Hooking;
using System.Threading.Tasks;

namespace clib.Extensions;

/// straight copy of https://github.com/MidoriKami/VanillaPlus/blob/5cb322123ace232ce0a656799ce14597ad23936e/VanillaPlus/Extensions/HookExtensions.cs#L11
public static class HookExtensions {
    extension<T>(Hook<T>? hook) where T : Delegate {
        public async Task EnableAsync() {
            if (hook is null) return;

            await IFramework.Get().Run(hook.Enable);
        }

        public async Task DisableAsync() {
            if (hook is null) return;

            await IFramework.Get().Run(hook.Disable);
        }

        public async Task DisposeAsync() {
            if (hook is null) return;

            await IFramework.Get().Run(hook.Dispose);
        }
    }
}
