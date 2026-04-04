using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Background service that intercepts playback start events and injects pre-roll videos
/// for clients that do NOT call the /Intros endpoint.
///
/// <list type="bullet">
///   <item><b>Roku</b> — sends a PlayNow command with [preRoll, originalItem] as a playlist.</item>
///   <item><b>Fire TV / Android TV</b> — sets Jellyfin's native ServerConfiguration.PreRollPath
///     so the server prepends the video at the transcode level (workaround; resets after 3 minutes).</item>
/// </list>
///
/// Standard clients (Jellyfin Web, iOS, Android, desktop) are handled by
/// <see cref="PreRollIntroProvider"/> and are skipped here to avoid double pre-rolls.
/// </summary>
public class SessionInterceptor : IHostedService, IDisposable
{
    // -----------------------------------------------------------------------
    // Client identification helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Clients that call /Intros themselves — IIntroProvider handles these, so we skip them.
    /// </summary>
    private static readonly string[] IntroProviderClientPrefixes =
    [
        "Jellyfin Web",
        "Jellyfin Media Player",
        "Jellyfin for iOS",
        "Jellyfin for Android",
        "Jellyfin Mobile",
        "Infuse",
        "Swiftfin"
    ];

    // -----------------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------------

    private readonly ISessionManager _sessionManager;
    private readonly IServerConfigurationManager _serverConfigManager;
    private readonly PreRollManager _manager;
    private readonly ILogger<SessionInterceptor> _logger;

    /// <summary>
    /// Tracks sessions+items we have already queued a pre-roll for.
    /// Key: "{sessionId}:{itemId}"
    /// Prevents re-processing when the original item starts playing after the pre-roll.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

    /// <summary>
    /// Tracks pre-roll item IDs that we intentionally enqueued.
    /// When PlaybackStart fires for one of these, we skip it instead of adding another pre-roll.
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
    // IHostedService
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.LogInformation("Pre-Roll Videos: SessionInterceptor started.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _logger.LogInformation("Pre-Roll Videos: SessionInterceptor stopped.");
        return Task.CompletedTask;
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
        // Clean up so the same item can get a pre-roll next time it plays.
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

        // --- 1. Skip if this is a pre-roll we scheduled (avoid recursion) ---
        if (_scheduledPreRolls.TryRemove(item.Id, out _))
        {
            _logger.LogDebug("Pre-Roll Videos: Skipping pre-roll item '{Name}' (scheduled).", item.Name);
            return;
        }

        // --- 2. Skip clients handled by IIntroProvider ---
        if (UsesIntroProvider(session))
        {
            _logger.LogDebug(
                "Pre-Roll Videos: Skipping session '{Client}' — handled by IIntroProvider.",
                session.Client);
            return;
        }

        // --- 3. Loop guard ---
        var key = BuildKey(session.Id, item.Id);
        if (!_processedKeys.TryAdd(key, 0))
        {
            _logger.LogDebug("Pre-Roll Videos: Already processed key '{Key}', skipping.", key);
            return;
        }

        // --- 4. Eligibility check ---
        if (!_manager.ShouldPlayPreRoll(item))
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        // --- 5. Pick a pre-roll ---
        var preRoll = _manager.GetRandomPreRoll();
        if (preRoll == null)
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        // --- 6. Dispatch by client type ---
        if (IsFireTvClient(session))
        {
            await HandleFireTvAsync(session, preRoll, item).ConfigureAwait(false);
        }
        else
        {
            // Roku, Kodi, and other interceptor-eligible clients
            await HandlePlaylistInjectionAsync(session, preRoll, item).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Injects pre-roll via a PlayNow command with [preRoll, originalItem] playlist.
    /// Works for Roku and similar clients.
    /// </summary>
    private async Task HandlePlaylistInjectionAsync(
        SessionInfo session,
        MediaBrowser.Controller.Entities.BaseItem preRoll,
        MediaBrowser.Controller.Entities.BaseItem item)
    {
        _logger.LogInformation(
            "Pre-Roll Videos [Interceptor/Playlist]: Queuing '{PreRoll}' before '{Item}' for session '{Client}'.",
            preRoll.Name, item.Name, session.Client);

        // Mark the pre-roll so its own PlaybackStart event is ignored.
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
                "Pre-Roll Videos [Interceptor/Playlist]: Failed to send play command to session '{Client}'.",
                session.Client);
            _scheduledPreRolls.TryRemove(preRoll.Id, out _);
        }
    }

    /// <summary>
    /// FireTV workaround: sets Jellyfin's native <c>ServerConfiguration.PreRollPath</c>
    /// to the chosen pre-roll video path. The server prepends this at the transcode level,
    /// bypassing the need for a SendPlayCommand which crashes the Fire TV client.
    /// The path is reset automatically after 3 minutes (longer than most pre-rolls).
    /// </summary>
    private Task HandleFireTvAsync(
        SessionInfo session,
        MediaBrowser.Controller.Entities.BaseItem preRoll,
        MediaBrowser.Controller.Entities.BaseItem item)
    {
        _logger.LogInformation(
            "Pre-Roll Videos [Interceptor/FireTV]: Setting native PreRollPath to '{PreRoll}' for session '{Client}'.",
            preRoll.Name, session.Client);

        var config = _serverConfigManager.Configuration;
        var previousPath = config.PreRollPath;

        config.PreRollPath = preRoll.Path;
        _serverConfigManager.SaveConfiguration();

        // Reset the native pre-roll path after a generous window.
        // This is a best-effort reset — if Jellyfin restarts, the saved config would persist
        // until the next reset, but realistically the path changes each playback anyway.
        _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(completedTask =>
        {
            try
            {
                // Only reset if it's still the value we set (another Fire TV session may have updated it).
                var current = _serverConfigManager.Configuration;
                if (string.Equals(current.PreRollPath, preRoll.Path, StringComparison.OrdinalIgnoreCase))
                {
                    current.PreRollPath = previousPath;
                    _serverConfigManager.SaveConfiguration();
                    _logger.LogDebug("Pre-Roll Videos [FireTV]: Native PreRollPath reset to '{Path}'.", previousPath ?? "(empty)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-Roll Videos [FireTV]: Failed to reset PreRollPath.");
            }
        }, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Client detection helpers
    // -----------------------------------------------------------------------

    private static bool UsesIntroProvider(SessionInfo session)
    {
        if (string.IsNullOrEmpty(session.Client)) return false;

        foreach (var prefix in IntroProviderClientPrefixes)
        {
            if (session.Client.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsFireTvClient(SessionInfo session)
    {
        var client = session.Client ?? string.Empty;
        var device = session.DeviceName ?? string.Empty;

        return client.Contains("Android TV", StringComparison.OrdinalIgnoreCase)
            || client.Contains("Fire TV", StringComparison.OrdinalIgnoreCase)
            || device.Contains("Fire TV", StringComparison.OrdinalIgnoreCase)
            || device.Contains("FireTV", StringComparison.OrdinalIgnoreCase)
            || device.StartsWith("AFT", StringComparison.OrdinalIgnoreCase); // Amazon Fire TV device prefix
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
        GC.SuppressFinalize(this);
    }
}
