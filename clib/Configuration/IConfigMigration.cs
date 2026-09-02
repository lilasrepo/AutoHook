using Dalamud.Configuration;

namespace clib.Configuration;

public interface IConfigMigration<T> where T : IPluginConfiguration {
    int TargetVersion { get; }
    void Migrate(T config);
}
