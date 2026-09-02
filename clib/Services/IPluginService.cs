namespace clib.Services;

/// <summary>
/// Marker for plugin singletons auto-registered in <see cref="CLibMain.Init"/>.
/// Disposed in <see cref="CLibMain.Dispose"/> / <see cref="CLibMain.DisposeAsync"/>.
/// Types that also implement <c>IPluginConfiguration</c> are loaded from the plugin config file when present.
/// </summary>
/// <remarks>
/// Instances are registered before constructors run, so constructors may <c>Get()</c> other plugin services.
/// </remarks>
public interface IPluginService;
