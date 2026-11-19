using Jellyfin.Plugin.Marquee.Api;
using Jellyfin.Plugin.Marquee.Configuration;
using Jellyfin.Plugin.Marquee.Scrobbling;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Marquee;

/// <summary>
/// Registers plugin services with Jellyfin's DI container.
/// The server picks this up automatically if the type implements
/// IPluginServiceRegistrator and lives in the plugin assembly.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient(HttpClientNames.Marquee);

        serviceCollection.AddSingleton<MarqueeClient>();
        serviceCollection.AddSingleton<DeviceCodeClient>();
        serviceCollection.AddSingleton<PlayedStateTracker>();
        serviceCollection.AddSingleton<ProgressPlayedTracker>();
        serviceCollection.AddSingleton<ProgressThrottle>();
        serviceCollection.AddSingleton<PayloadBuilder>();

        // Hosted services: Jellyfin starts/stops them with the server.
        serviceCollection.AddHostedService<ScrobbleManager>();
    }
}

public static class HttpClientNames
{
    public const string Marquee = "Marquee";
}
