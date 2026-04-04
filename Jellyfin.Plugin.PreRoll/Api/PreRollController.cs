using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PreRoll.Api;

/// <summary>
/// API controller for the Pre-Roll Videos plugin configuration page.
/// </summary>
[ApiController]
[Route("PreRoll")]
[Authorize(Policy = "RequiresElevation")]
public class PreRollController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRollController"/> class.
    /// </summary>
    public PreRollController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Returns all top-level Jellyfin libraries available for selection.
    /// Used by the config page to populate both the "pre-roll source" and
    /// "target content libraries" dropdowns.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LibraryDto>> GetLibraries()
    {
        var folders = _libraryManager.GetUserRootFolder().Children
            .OfType<MediaBrowser.Controller.Entities.Folder>()
            .OrderBy(f => f.Name)
            .Select(f =>
            {
                // CollectionType may be string? or CollectionTypeOptions? depending on Jellyfin version.
                // Convert to string safely via the object's ToString() — works for both.
                var collectionType = f.CollectionType is null
                    ? "unknown"
                    : f.CollectionType.ToString()!.ToLowerInvariant();

                return new LibraryDto
                {
                    Id = f.Id.ToString("N"),
                    Name = f.Name,
                    CollectionType = collectionType
                };
            });

        return Ok(folders);
    }
}

/// <summary>
/// DTO representing a Jellyfin library for the config page.
/// </summary>
public sealed class LibraryDto
{
    /// <summary>Gets or sets the library ID (no hyphens).</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the collection type (movies, tvshows, videos, etc.).</summary>
    [Required]
    public string CollectionType { get; set; } = string.Empty;
}
