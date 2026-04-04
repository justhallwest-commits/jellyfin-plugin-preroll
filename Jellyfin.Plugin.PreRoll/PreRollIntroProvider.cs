using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Provides pre-roll videos via the standard Jellyfin IIntroProvider interface.
/// Handles clients that call /Users/{userId}/Items/{itemId}/Intros:
/// Jellyfin Web, iOS, Android, Jellyfin Media Player, Swiftfin, Infuse.
///
/// Because Jellyfin auto-discovers and DI-instantiates this class, its constructor
/// is also the ideal place to start the <see cref="SessionInterceptor"/> for
/// non-/Intros clients (Roku, Fire TV) without needing IPluginServiceRegistrator.
/// </summary>
public class PreRollIntroProvider : IIntroProvider
{
    private readonly PreRollManager _manager;
    private readonly ILogger<PreRollIntroProvider> _logger;

    // Tracked so we only start one interceptor even if Jellyfin instantiates
    // this class more than once (unlikely but defensive).
    private static SessionInterceptor? _interceptor;
    private static readonly object _interceptorLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollIntroProvider"/> class.
    /// All parameters are injected by Jellyfin's DI container.
    /// </summary>
    public PreRollIntroProvider(
        ILibraryManager libraryManager,
        ISessionManager sessionManager,
        IServerConfigurationManager serverConfigManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PreRollIntroProvider>();
        _manager = new PreRollManager(libraryManager, loggerFactory.CreateLogger<PreRollManager>());

        // Store on Plugin.Instance so other code can reach it.
        if (Plugin.Instance != null)
        {
            Plugin.Instance.Manager = _manager;
        }

        // Start the session interceptor exactly once.
        lock (_interceptorLock)
        {
            if (_interceptor == null)
            {
                _interceptor = new SessionInterceptor(
                    sessionManager,
                    serverConfigManager,
                    _manager,
                    loggerFactory.CreateLogger<SessionInterceptor>());

                _interceptor.Start();
                _logger.LogInformation("Pre-Roll Videos: SessionInterceptor started from PreRollIntroProvider.");
            }
        }
    }

    /// <inheritdoc />
    public string Name => "Pre-Roll Videos";

    /// <inheritdoc />
    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        if (!_manager.ShouldPlayPreRoll(item))
        {
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        var preRoll = _manager.GetRandomPreRoll();
        if (preRoll == null)
        {
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        _logger.LogInformation(
            "Pre-Roll Videos [IIntroProvider]: Queuing '{PreRoll}' before '{Item}'",
            preRoll.Name,
            item.Name);

        IEnumerable<IntroInfo> result = new[]
        {
            new IntroInfo
            {
                ItemId = preRoll.Id,
                Path = preRoll.Path
            }
        };

        return Task.FromResult(result);
    }
}
