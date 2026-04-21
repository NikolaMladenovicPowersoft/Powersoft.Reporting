namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Lightweight metadata about a saved report layout (named/public).
/// Used by the layout picker UI on report pages.
/// </summary>
public sealed class SavedLayoutInfo
{
    /// <summary>
    /// Full INI header code (e.g. "CATALOGUE:my-layout"). Used as the lookup key when loading.
    /// </summary>
    public string HeaderCode { get; set; } = string.Empty;

    /// <summary>
    /// Human-friendly layout name. Stored in tbl_IniHeader.IniHeaderDescr.
    /// For the legacy single-default layout (HeaderCode = "CATALOGUE"), this is "Default".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True if visible to all users (fk_UserCode IS NULL). False if owned by a single user.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// User code of the original creator. For private layouts equals the owner; for public layouts
    /// indicates who may overwrite/delete the layout.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// True if the current viewer can overwrite/delete this layout.
    /// (own private layouts: always; public layouts: only if CreatedBy = currentUser).
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// Last modification timestamp (UTC).
    /// </summary>
    public DateTime? LastModified { get; set; }
}
