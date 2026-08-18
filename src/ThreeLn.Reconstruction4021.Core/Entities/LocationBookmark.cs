using ThreeLn.Reconstruction4021.Core.Galaxy;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// A player-assigned label for a map coordinate or object (NAMES.PAS — confirmed to be simple
/// user-typed bookmarks, not a procedural name generator).
/// </summary>
public sealed class LocationBookmark
{
    public required string Name { get; set; }
    public required Coordinate Location { get; set; }
}
