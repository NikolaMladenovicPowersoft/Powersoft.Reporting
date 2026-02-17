using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

/// <summary>
/// Repository for item lookup used in report filters (e.g. Items Selection).
/// </summary>
public interface IItemRepository
{
    /// <summary>
    /// Search items by code or name. Returns active items only by default.
    /// </summary>
    /// <param name="search">Search term (matched against ItemCode and ItemNamePrimary). Empty = return all.</param>
    /// <param name="includeInactive">If true, include inactive items in results.</param>
    /// <param name="maxResults">Maximum number of results to return (default 200).</param>
    Task<List<Item>> SearchItemsAsync(string? search, bool includeInactive = false, int maxResults = 200);
}
