using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Registers plugin services into Jellyfin's DI container.
/// Called automatically by Jellyfin on plugin load.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        // Core manager — singleton so the Random instance is shared
        serviceCollection.AddSingleton<PreRollManager>();

        // IIntroProvider — handles Web, iOS, Android, desktop clients
        serviceCollection.AddSingleton<IIntroProvider, PreRollIntroProvider>();

        // SessionInterceptor — handles Roku, Fire TV, and other non-/Intros clients
        serviceCollection.AddHostedService<SessionInterceptor>();

        // Note: PreRollController is auto-registered by Jellyfin's ASP.NET Core pipeline.
    }
}
