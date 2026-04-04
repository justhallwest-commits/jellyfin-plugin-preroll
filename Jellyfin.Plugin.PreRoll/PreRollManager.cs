using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Manages pre-roll video selection and eligibility checks.
/// </summary>
public class PreRollManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollManager"/> class.
    /// </summary>
    public PreRollManager(ILibraryManager libraryManager, ILogger<PreRollManager> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns a randomly selected pre-roll item, or null if none are available.
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
            // No IncludeItemTypes filter — BaseItemKind moved in 10.11.
            // The pre-roll library should only contain video files anyway.
            var results = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = libraryId,
                IsVirtualItem = false,
                Recursive = true,
                HasPath = true
            });
            items.AddRange(results.Where(i => !string.IsNullOrEmpty(i.Path)));
        }

        if (items.Count == 0)
        {
            _logger.LogWarning("Pre-Roll Videos: Pre-roll libraries configured but contain no items.");
            return null;
        }

        var selected = items[Random.Shared.Next(items.Count)];
        _logger.LogDebug("Pre-Roll Videos: Selected '{Name}' (Id={Id})", selected.Name, selected.Id);
        return selected;
    }

    /// <summary>
    /// Returns true if a pre-roll should play before this item.
    /// </summary>
    public bool ShouldPlayPreRoll(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var config = PreRollPlugin.Instance?.Configuration;
        if (config == null) return false;

        var isMovie = item is Movie;
        var isEpisode = item is Episode;

        if (!isMovie && !isEpisode) return false;
        if (isMovie && !config.EnableForMovies) return false;
        if (isEpisode && !config.EnableForTvShows) return false;

        if (config.EnableForAllLibraries) return true;

        var targetIds = config.TargetLibraryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToHashSet();

        if (targetIds.Count == 0) return true;

        var parent = item.GetParent();
        while (parent != null)
        {
            if (targetIds.Contains(parent.Id)) return true;
            parent = parent.GetParent();
        }

        return false;
    }

    private Guid[] GetPreRollLibraryIds()
    {
        var config = PreRollPlugin.Instance?.Configuration;
        if (config == null) return [];

        return config.PreRollLibraryIds
            .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToArray();
    }
}
