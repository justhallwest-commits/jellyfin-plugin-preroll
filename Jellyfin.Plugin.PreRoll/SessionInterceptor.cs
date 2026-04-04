using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Intercepts playback start events and injects pre-roll videos for ALL clients.
///
/// <list type="bullet">
///   <item><b>All standard clients</b> (Web, iOS, Android, Roku, desktop) —
///     sends a PlayNow command with [preRoll, originalItem] as a playlist.</item>
///   <item><b>Fire TV / Android TV</b> — sets Jellyfin's native ServerConfiguration.PreRollPath
///     (workaround; SendPlayCommand crashes Fire TV). Resets after 3 minutes.</item>
/// </list>
///
/// Started directly from <see cref="Plugin"/>'s constructor.
/// </summary>
public class SessionInterceptor : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IServerConfigurationManager _serverConfigManager;
    private readonly PreRollManager _manager;
    private readonly ILogger<SessionInterceptor> _logger;

    /// <summary>
    /// Tracks session+item combos already queued to prevent double pre-rolls.
    /// Key: "{sessionId}:{itemId}"
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

    /// <summary>
    /// Tracks pre-roll item IDs we intentionally enqueued.
    /// Their PlaybackStart event is skipped to avoid recursion.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _scheduledPreRolls = new();

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionInterceptor"/> class.
    /// </summary>
    public SessionInterceptor(
        ISessionManager sessionManager,
        IServerConfigurationManager serverConfigManager,
        PreRollManager manager,
        ILogger<SessionInterceptor> logger)
    {
        _sessionManager = sessionManager;
        _serverConfigManager = serverConfigManager;
        _manager = manager;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    /// <summary>Subscribes to session events. Called once at plugin startup.</summary>
    public void Start()
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.LogInformation("Pre-Roll Videos: SessionInterceptor started.");
    }

    /// <summary>Unsubscribes from session events.</summary>
    public void Stop()
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _logger.LogInformation("Pre-Roll Videos: SessionInterceptor stopped.");
    }

    // -----------------------------------------------------------------------
    // Event handlers
    // -----------------------------------------------------------------------

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            await HandlePlaybackStartAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pre-Roll Videos: Unhandled error in OnPlaybackStart.");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item != null)
        {
            var key = BuildKey(e.Session.Id, e.Item.Id);
            _processedKeys.TryRemove(key, out _);
        }
    }

    // -----------------------------------------------------------------------
    // Core logic
    // -----------------------------------------------------------------------

    private async Task HandlePlaybackStartAsync(PlaybackProgressEventArgs e)
    {
        var item = e.Item;
        var session = e.Session;

        if (item == null || session == null) return;

        // 1. Skip if this is a pre-roll we scheduled (avoid recursion).
        if (_scheduledPreRolls.TryRemove(item.Id, out _))
        {
            _logger.LogDebug("Pre-Roll Videos: Skipping scheduled pre-roll item '{Name}'.", item.Name);
            return;
        }

        // 2. Loop guard — prevents re-processing when the original item
        //    starts playing after the pre-roll finishes.
        var key = BuildKey(session.Id, item.Id);
        if (!_processedKeys.TryAdd(key, 0))
        {
            _logger.LogDebug("Pre-Roll Videos: Already processed '{Key}', skipping.", key);
            return;
        }

        // 3. Eligibility check (movie/episode, library filter).
        if (!_manager.ShouldPlayPreRoll(item))
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        // 4. Pick a pre-roll.
        var preRoll = _manager.GetRandomPreRoll();
        if (preRoll == null)
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        // 5. Dispatch by client type.
        if (IsFireTvClient(session))
        {
            HandleFireTv(session, preRoll);
        }
        else
        {
            await HandlePlaylistInjectionAsync(session, preRoll, item).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a PlayNow command with [preRoll, originalItem].
    /// Works for all standard clients: Web, iOS, Android, Roku, desktop.
    /// </summary>
    private async Task HandlePlaylistInjectionAsync(
        SessionInfo session,
        MediaBrowser.Controller.Entities.BaseItem preRoll,
        MediaBrowser.Controller.Entities.BaseItem item)
    {
        _logger.LogInformation(
            "Pre-Roll Videos: Queuing '{PreRoll}' before '{Item}' on '{Client}'.",
            preRoll.Name, item.Name, session.Client);

        // Mark the pre-roll so its own PlaybackStart is ignored.
        _scheduledPreRolls.TryAdd(preRoll.Id, 0);

        try
        {
            await _sessionManager.SendPlayCommand(
                session.Id,
                session.Id,
                new PlayRequest
                {
                    ItemIds = [preRoll.Id, item.Id],
                    PlayCommand = PlayCommand.PlayNow,
                    StartIndex = 0
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Pre-Roll Videos: Failed to send play command to '{Client}'.",
                session.Client);
            _scheduledPreRolls.TryRemove(preRoll.Id, out _);
        }
    }

    /// <summary>
    /// Fire TV workaround: sets Jellyfin's native ServerConfiguration.PreRollPath
    /// so the server prepends the video at the transcode level.
    /// Resets after 3 minutes.
    /// </summary>
    private void HandleFireTv(
        SessionInfo session,
        MediaBrowser.Controller.Entities.BaseItem preRoll)
    {
        _logger.LogInformation(
            "Pre-Roll Videos [FireTV]: Setting PreRollPath to '{PreRoll}' for '{Client}'.",
            preRoll.Name, session.Client);

        var config = _serverConfigManager.Configuration;
        var previousPath = config.PreRollPath;

        config.PreRollPath = preRoll.Path;
        _serverConfigManager.SaveConfiguration();

        _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(completedTask =>
        {
            try
            {
                var current = _serverConfigManager.Configuration;
                if (string.Equals(current.PreRollPath, preRoll.Path, StringComparison.OrdinalIgnoreCase))
                {
                    current.PreRollPath = previousPath;
                    _serverConfigManager.SaveConfiguration();
                    _logger.LogDebug(
                        "Pre-Roll Videos [FireTV]: PreRollPath reset to '{Path}'.",
                        previousPath ?? "(empty)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-Roll Videos [FireTV]: Failed to reset PreRollPath.");
            }
        }, TaskScheduler.Default);
    }

    // -----------------------------------------------------------------------
    // Client detection
    // -----------------------------------------------------------------------

    private static bool IsFireTvClient(SessionInfo session)
    {
        var client = session.Client ?? string.Empty;
        var device = session.DeviceName ?? string.Empty;

        return client.Contains("Android TV", StringComparison.OrdinalIgnoreCase)
            || client.Contains("Fire TV", StringComparison.OrdinalIgnoreCase)
            || device.Contains("Fire TV", StringComparison.OrdinalIgnoreCase)
            || device.Contains("FireTV", StringComparison.OrdinalIgnoreCase)
            || device.StartsWith("AFT", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildKey(string sessionId, Guid itemId) => $"{sessionId}:{itemId}";

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
