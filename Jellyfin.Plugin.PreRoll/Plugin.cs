using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Pre-Roll Videos plugin entry point.
/// Starts the SessionInterceptor from its constructor — the reliable
/// startup hook in Jellyfin 10.11 without IPluginServiceRegistrator.
/// </summary>
public class PreRollPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Plugin GUID — must stay constant across releases.</summary>
    public static readonly Guid PluginGuid = new("a4b2c3d4-e5f6-7890-abcd-ef1234567891");

    private readonly SessionInterceptor _interceptor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollPlugin"/> class.
    /// </summary>
    public PreRollPlugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        var manager = new PreRollManager(libraryManager, loggerFactory.CreateLogger<PreRollManager>());
        Manager = manager;

        _interceptor = new SessionInterceptor(
            sessionManager,
            manager,
            loggerFactory.CreateLogger<SessionInterceptor>());

        _interceptor.Start();
    }

    /// <inheritdoc />
    public override string Name => "Pre-Roll Videos";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <inheritdoc />
    public override string Description => "Plays pre-roll videos before movies and TV episodes across all Jellyfin clients.";

    /// <summary>Gets the singleton plugin instance.</summary>
    public static PreRollPlugin? Instance { get; private set; }

    /// <summary>Gets the shared pre-roll manager.</summary>
    public PreRollManager? Manager { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
