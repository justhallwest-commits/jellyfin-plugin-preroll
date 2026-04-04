using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Plugin configuration persisted to XML.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class with defaults.
    /// </summary>
    public PluginConfiguration()
    {
        PreRollLibraryIds = [];
        EnableForMovies = true;
        EnableForTvShows = true;
        EnableForAllLibraries = true;
        TargetLibraryIds = [];
    }

    /// <summary>
    /// Gets or sets the IDs of Jellyfin libraries that contain pre-roll video files.
    /// Add your pre-roll folder as a "Videos" library in Jellyfin and select it here.
    /// </summary>
    public string[] PreRollLibraryIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether pre-rolls play before movies.
    /// </summary>
    public bool EnableForMovies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether pre-rolls play before TV episodes.
    /// </summary>
    public bool EnableForTvShows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether pre-rolls apply to ALL content libraries.
    /// When false, only libraries in <see cref="TargetLibraryIds"/> receive pre-rolls.
    /// </summary>
    public bool EnableForAllLibraries { get; set; }

    /// <summary>
    /// Gets or sets the IDs of content libraries that should receive pre-rolls.
    /// Only used when <see cref="EnableForAllLibraries"/> is false.
    /// </summary>
    public string[] TargetLibraryIds { get; set; }
}
