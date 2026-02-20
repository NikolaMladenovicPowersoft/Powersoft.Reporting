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
}
