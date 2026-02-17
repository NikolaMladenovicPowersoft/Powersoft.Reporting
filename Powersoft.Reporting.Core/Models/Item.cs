namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Represents an item for report filtering (e.g. Average Basket item selection).
/// Maps to tbl_Item in tenant database.
/// </summary>
public class Item
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemNamePrimary { get; set; } = string.Empty;
    public string? ItemNameSecondary { get; set; }
    public bool Active { get; set; }
    
    public string DisplayName => string.IsNullOrEmpty(ItemNameSecondary)
        ? $"{ItemCode} - {ItemNamePrimary}"
        : $"{ItemCode} - {ItemNamePrimary}";
}
