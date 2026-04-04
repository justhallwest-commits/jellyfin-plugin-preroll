using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Manages pre-roll video selection and eligibility checks.
/// </summary>
public class PreRollManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollManager> _logger;
    private readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollManager"/> class.
    /// </summary>
    public PreRollManager(ILibraryManager libraryManager, ILogger<PreRollManager> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns a randomly selected pre-roll item from the configured pre-roll libraries,
    /// or <c>null</c> if none are configured or available.
    /// </summary>
    public BaseItem? GetRandomPreRoll()
    {
        var libraryIds = GetPreRollLibraryIds();
        if (libraryIds.Length == 0)
        {
            _logger.LogDebug("Pre-Roll Videos: No pre-roll libraries configured.");
            return null;
        }

        var items = new List<BaseItem>();
        foreach (var libraryId in libraryIds)
        {
            var results = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = libraryId,
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Video },
                IsVirtualItem = false,
                Recursive = true
            });
            items.AddRange(results);
        }

        if (items.Count == 0)
        {
            _logger.LogWarning("Pre-Roll Videos: Pre-roll libraries are configured but contain no video items.");
            return null;
        }

        var selected = items[_random.Next(items.Count)];
        _logger.LogDebug("Pre-Roll Videos: Selected pre-roll '{Name}' (Id={Id})", selected.Name, selected.Id);
        return selected;
    }

    /// <summary>
    /// Returns <c>true</c> if a pre-roll should play before <paramref name="item"/>.
    /// </summary>
    public bool ShouldPlayPreRoll(BaseItem item)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null) return false;

        // Type checks
        var isMovie = item is Movie;
        var isEpisode = item is Episode;

        if (!isMovie && !isEpisode) return false;
        if (isMovie && !config.EnableForMovies) return false;
        if (isEpisode && !config.EnableForTvShows) return false;

        if (config.EnableForAllLibraries) return true;

        // Check whether the item lives in a target library
        var targetIds = config.TargetLibraryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToHashSet();

        if (targetIds.Count == 0) return true;

        // Walk up the item's parent chain to find the library root
        var parent = item.GetParent();
        while (parent != null)
        {
            if (targetIds.Contains(parent.Id)) return true;
            parent = parent.GetParent();
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="item"/> is itself a pre-roll video
    /// (i.e. lives inside a configured pre-roll library). Used by the session interceptor
    /// to avoid recursively adding pre-rolls before pre-rolls.
    /// </summary>
    public bool IsPreRollItem(BaseItem item)
    {
        var preRollIds = GetPreRollLibraryIds().ToHashSet();
        if (preRollIds.Count == 0) return false;

        var parent = item.GetParent();
        while (parent != null)
        {
            if (preRollIds.Contains(parent.Id)) return true;
            parent = parent.GetParent();
        }

        return false;
    }

    // -------------------------------------------------------------------------

    private Guid[] GetPreRollLibraryIds()
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null) return [];

        return config.PreRollLibraryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToArray();
    }
}
