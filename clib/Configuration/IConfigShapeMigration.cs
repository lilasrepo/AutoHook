using Dalamud.Configuration;

namespace clib.Configuration;

public interface IConfigShapeMigration<TFrom, TTo> where TFrom : IPluginConfiguration where TTo : IPluginConfiguration {
    int TargetVersion { get; }
    TTo Migrate(TFrom from);
}
