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
    /// Returns all virtual folders (libraries) for the config page.
    /// Uses GetVirtualFolders() which returns CollectionType correctly in Jellyfin 10.11.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LibraryDto>> GetLibraries()
    {
        var folders = _libraryManager.GetVirtualFolders()
            .OrderBy(f => f.Name)
            .Select(f => new LibraryDto
            {
                Id = f.ItemId ?? string.Empty,
                Name = f.Name ?? string.Empty,
                CollectionType = f.CollectionType ?? "unknown"
            });

        return Ok(folders);
    }
}

/// <summary>
/// DTO representing a Jellyfin library for the config page.
/// </summary>
public sealed class LibraryDto
{
    /// <summary>Gets or sets the library ID.</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the collection type (movies, tvshows, videos, etc.).</summary>
    [Required]
    public string CollectionType { get; set; } = string.Empty;
}
