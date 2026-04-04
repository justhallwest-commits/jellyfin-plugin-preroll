using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Provides pre-roll videos via the standard Jellyfin IIntroProvider interface.
/// This handles clients that call the /Users/{userId}/Items/{itemId}/Intros endpoint:
/// Jellyfin Web (browser), Jellyfin for iOS, Jellyfin for Android, Jellyfin Media Player.
/// </summary>
public class PreRollIntroProvider : IIntroProvider
{
    private readonly PreRollManager _manager;
    private readonly ILogger<PreRollIntroProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollIntroProvider"/> class.
    /// </summary>
    public PreRollIntroProvider(PreRollManager manager, ILogger<PreRollIntroProvider> logger)
    {
        _manager = manager;
        _logger = logger;
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
            "Pre-Roll Videos [IIntroProvider]: Queuing '{PreRollName}' before '{ItemName}'",
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
