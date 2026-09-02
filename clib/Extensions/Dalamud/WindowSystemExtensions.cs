using Dalamud.Interface.Windowing;
using System.Diagnostics.CodeAnalysis;

namespace clib.Extensions;

public static class WindowSystemExtensions {
    extension(WindowSystem ws) {
        public Window? GetWindow<T>() where T : Window => ws.Windows.OfType<T>().FirstOrDefault();
        public bool TryGetWindow<T>([NotNullWhen(true)] out Window? window) where T : Window {
            window = ws.GetWindow<T>();
            return window != null;
        }
        public void Toggle<T>() where T : Window => GetWindow<T>(ws)?.IsOpen ^= true;
        public void RemoveWindow<T>() where T : Window {
            if (TryGetWindow<T>(ws, out var window)) {
                window.IsOpen = false;
                ws.RemoveWindow(window);
            }
        }
    }
}
