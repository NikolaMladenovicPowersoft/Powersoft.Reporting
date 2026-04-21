using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IIniRepository
{
    /// <summary>
    /// Loads all INI parameters for a given module + user.
    /// Returns empty dictionary if no saved layout exists.
    /// </summary>
    Task<Dictionary<string, string>> GetLayoutAsync(string moduleCode, string headerCode, string userCode);

    /// <summary>
    /// Saves layout parameters. Creates header if not exists, replaces all details.
    /// Matches legacy RAS_AddBulkIniDetail pattern: find header → delete old details → insert new.
    /// </summary>
    Task SaveLayoutAsync(string moduleCode, string headerCode, string headerDescription,
        string userCode, Dictionary<string, string> parameters);

    /// <summary>
    /// Deletes the user's saved layout for a specific module/header.
    /// After this, the report falls back to hardcoded defaults.
    /// </summary>
    Task<bool> DeleteLayoutAsync(string moduleCode, string headerCode, string userCode);

    // -------------------- Named / public layouts (multi per user) --------------------

    /// <summary>
    /// Lists all available layouts for a module visible to <paramref name="userCode"/>.
    /// Includes:
    ///  - the user's own layouts (header code prefix match, fk_UserCode = userCode)
    ///  - public layouts (fk_UserCode IS NULL)
    /// Sorted: own first, then public; alphabetical by name within each group.
    /// </summary>
    Task<IReadOnlyList<SavedLayoutInfo>> ListLayoutsAsync(string moduleCode, string headerCodePrefix, string userCode);

    /// <summary>
    /// Saves a named layout. If <paramref name="isPublic"/> is true, the layout is saved with fk_UserCode = NULL
    /// and visible to all users; only the original CreatedBy may overwrite/delete it.
    /// Returns the resolved header code that was used (so the caller can re-load by it).
    /// Throws InvalidOperationException if attempting to overwrite a public layout owned by a different user.
    /// </summary>
    Task<string> SaveNamedLayoutAsync(string moduleCode, string headerCodePrefix, string headerDescription,
        string userCode, string layoutName, bool isPublic, Dictionary<string, string> parameters);

    /// <summary>
    /// Loads a named layout by header code. Returns empty dictionary if not found or not visible to the user.
    /// </summary>
    Task<Dictionary<string, string>> GetNamedLayoutAsync(string moduleCode, string headerCode, string userCode);

    /// <summary>
    /// Deletes a named layout. For public layouts, only CreatedBy = userCode may delete.
    /// Returns true if a layout was deleted.
    /// </summary>
    Task<bool> DeleteNamedLayoutAsync(string moduleCode, string headerCode, string userCode);
}
