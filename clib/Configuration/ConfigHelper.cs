using clib.Services;
using Dalamud.Configuration;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace clib.Configuration;

public static class ConfigHelper {
    private static readonly string[] ConfigExtensions = [".json", ".yaml", ".yml"];

    public static bool RunMigrationChain(IPluginConfiguration config, Assembly? assembly, out IPluginConfiguration final, bool makeBackups = true) {
        assembly ??= config.GetType().Assembly;
        final = config;
        var migrated = false;

        while (true) {
            var version = GetVersion(final);

            if (TryGetNextInPlaceMigration(final.GetType(), assembly, version, out var inPlace, out var inPlaceTarget)) {
                if (makeBackups)
                    BackupMigration(version, inPlaceTarget);

                IPluginLog.Get().Info($"Migrating config from version {version} to {inPlaceTarget}");
                InvokeInPlaceMigration(inPlace, final);
                SetVersion(final, inPlaceTarget);
                migrated = true;
                continue;
            }

            if (TryGetNextShapeMigration(final.GetType(), assembly, version, out var shape, out var shapeTarget)) {
                if (makeBackups)
                    BackupMigration(version, shapeTarget);

                IPluginLog.Get().Info($"Migrating config from version {version} to {shapeTarget}");
                final = InvokeShapeMigration(shape, final);
                SetVersion(final, shapeTarget);
                migrated = true;
                continue;
            }

            break;
        }

        return migrated;
    }

    public static bool Migrate<T>(T config, Assembly? assembly = null, bool makeBackups = true) where T : IPluginConfiguration {
        var migrated = RunMigrationChain(config, assembly ?? typeof(T).Assembly, out var final, makeBackups);
        if (!ReferenceEquals(final, config) && final is not T)
            throw new InvalidOperationException($"Config shape changed to {final.GetType().Name}; use {nameof(RunMigrationChain)} instead.");
        return migrated;
    }

    private static bool TryGetNextInPlaceMigration(Type configType, Assembly assembly, int version, out object migration, out int targetVersion) {
        migration = null!;
        targetVersion = 0;

        object? best = null;
        var bestTarget = int.MaxValue;

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false })) {
            var migrationType = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConfigMigration<>));
            if (migrationType?.GetGenericArguments()[0] != configType)
                continue;

            var instance = CreateInstance(type);
            var target = (int)migrationType.GetProperty(nameof(IConfigMigration<>.TargetVersion))!.GetValue(instance)!;
            if (target <= version || target >= bestTarget)
                continue;

            best = instance;
            bestTarget = target;
        }

        if (best == null)
            return false;

        migration = best;
        targetVersion = bestTarget;
        return true;
    }

    private static bool TryGetNextShapeMigration(Type configType, Assembly assembly, int version, out object migration, out int targetVersion) {
        migration = null!;
        targetVersion = 0;

        object? best = null;
        var bestTarget = int.MaxValue;
        Type? bestInterface = null;

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false })) {
            var shapeType = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConfigShapeMigration<,>));
            if (shapeType?.GetGenericArguments()[0] != configType)
                continue;

            var instance = CreateInstance(type);
            var target = (int)shapeType.GetProperty(nameof(IConfigShapeMigration<,>.TargetVersion))!.GetValue(instance)!;
            if (target <= version || target >= bestTarget)
                continue;

            best = instance;
            bestTarget = target;
            bestInterface = shapeType;
        }

        if (best == null)
            return false;

        migration = new ShapeMigrationInvoker(best, bestInterface!);
        targetVersion = bestTarget;
        return true;
    }

    private static void InvokeInPlaceMigration(object migration, IPluginConfiguration config) {
        var migrationType = migration.GetType().GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConfigMigration<>));
        migrationType.GetMethod(nameof(IConfigMigration<>.Migrate))!.Invoke(migration, [config]);
    }

    private static IPluginConfiguration InvokeShapeMigration(object migration, IPluginConfiguration config)
        => migration is ShapeMigrationInvoker invoker ? invoker.Migrate(config) : throw new InvalidOperationException($"Invalid {nameof(ShapeMigrationInvoker)}");

    private sealed class ShapeMigrationInvoker(object migration, Type migrationInterface) {
        public IPluginConfiguration Migrate(IPluginConfiguration from)
            => (IPluginConfiguration)migrationInterface.GetMethod(nameof(IConfigShapeMigration<,>.Migrate))!.Invoke(migration, [from])!;
    }

    private static object CreateInstance(Type type) {
        if (Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, args: null, culture: null) is not { } instance)
            throw new InvalidOperationException($"Failed to create migration instance for {type.FullName}.");
        return instance;
    }

    private static int GetVersion(object config) {
        var type = config.GetType();
        var prop = type.GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.PropertyType == typeof(int))
            return (int)prop.GetValue(config)!;

        var field = type.GetField("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType == typeof(int))
            return (int)field.GetValue(config)!;

        return 0;
    }

    private static void SetVersion(object config, int version) {
        var type = config.GetType();
        var prop = type.GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.PropertyType == typeof(int) && prop.CanWrite) {
            prop.SetValue(config, version);
            return;
        }

        var field = type.GetField("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType == typeof(int))
            field.SetValue(config, version);
    }

    private static void BackupMigration(int fromVersion, int toVersion) {
        try {
            if (Svc.Interface.ConfigDirectory is not { Exists: true } cfgDir)
                return;

            var pluginName = Svc.Interface.Manifest.Name;
            var backupDir = Path.Join(cfgDir.Parent!.Parent!.FullName, "backups", pluginName);
            Directory.CreateDirectory(backupDir);

            var archiveName = $"{pluginName}.v{fromVersion}-to-v{toVersion}.zip";
            var tempFile = Path.Join(backupDir, $"{pluginName}.v{fromVersion}-to-v{toVersion}.tmp.zip");
            var archivePath = Path.Join(backupDir, archiveName);

            ZipFile.CreateFromDirectory(cfgDir.FullName, tempFile);
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            File.Move(tempFile, archivePath);
        }
        catch (Exception ex) {
            IPluginLog.Get().Warning(ex, "Migration backup skipped");
        }
    }
}
