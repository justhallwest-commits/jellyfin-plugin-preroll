using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Intercepts playback start events and injects pre-roll videos for all clients
/// via a PlayNow command with [preRoll, originalItem] as a two-item playlist.
///
/// Note: ServerConfiguration.PreRollPath was removed in Jellyfin 10.11,
/// so all clients including Fire TV receive SendPlayCommand. If a Fire TV
/// client crashes on PlayNow, it will be caught and logged without affecting
/// other sessions.
/// </summary>
public sealed class SessionInterceptor : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly PreRollManager _manager;
    private readonly ILogger<SessionInterceptor> _logger;

    /// <summary>
    /// Tracks session+item combos already handled.
    /// Key: "{sessionId}:{itemId}"
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

    /// <summary>
    /// Tracks pre-roll item IDs we intentionally enqueued so their own
    /// PlaybackStart is skipped to avoid recursion.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _scheduledPreRolls = new();

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionInterceptor"/> class.
    /// </summary>
    public SessionInterceptor(
        ISessionManager sessionManager,
        PreRollManager manager,
        ILogger<SessionInterceptor> logger)
    {
        _sessionManager = sessionManager;
        _manager = manager;
        _logger = logger;
    }

    /// <summary>Subscribes to session events.</summary>
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
            _processedKeys.TryRemove(BuildKey(e.Session.Id, e.Item.Id), out _);
        }
    }

    private async Task HandlePlaybackStartAsync(PlaybackProgressEventArgs e)
    {
        var item = e.Item;
        var session = e.Session;
        if (item == null || session == null) return;

        // Skip if this is a pre-roll we scheduled (prevents recursion).
        if (_scheduledPreRolls.TryRemove(item.Id, out _))
        {
            _logger.LogDebug("Pre-Roll Videos: Skipping scheduled pre-roll '{Name}'.", item.Name);
            return;
        }

        // Loop guard: skip if we already handled this session+item combo.
        var key = BuildKey(session.Id, item.Id);
        if (!_processedKeys.TryAdd(key, 0))
        {
            _logger.LogDebug("Pre-Roll Videos: Already processed '{Key}', skipping.", key);
            return;
        }

        // Eligibility check.
        if (!_manager.ShouldPlayPreRoll(item))
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        // Pick a random pre-roll.
        var preRoll = _manager.GetRandomPreRoll();
        if (preRoll == null)
        {
            _processedKeys.TryRemove(key, out _);
            return;
        }

        _logger.LogInformation(
            "Pre-Roll Videos: Queuing '{PreRoll}' before '{Item}' on client '{Client}'.",
            preRoll.Name,
            item.Name,
            session.Client);

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
                "Pre-Roll Videos: SendPlayCommand failed for client '{Client}'. Pre-roll skipped.",
                session.Client);
            _scheduledPreRolls.TryRemove(preRoll.Id, out _);
        }
    }

    private static string BuildKey(string sessionId, Guid itemId) => $"{sessionId}:{itemId}";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
