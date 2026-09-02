using clib.Services;

namespace clib.Extensions;

public static class IPluginServiceExtensions {
    extension<T>(T) where T : class, IPluginService {
        public static T Get() => Svc.Get<T>();
    }
}
