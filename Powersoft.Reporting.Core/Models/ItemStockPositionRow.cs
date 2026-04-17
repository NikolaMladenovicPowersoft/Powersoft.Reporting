namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Per-store stock breakdown for a single item.
/// Mirrors original Powersoft365 Item Stock Position dialog (ItemStockPosition.aspx + WQR.LoadStockPerStoreForItemWithShelf).
/// </summary>
public class ItemStockPositionRow
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public decimal OnStock { get; set; }
    public decimal OnTransfer { get; set; }
    public decimal Reserved { get; set; }
    public decimal Ordered { get; set; }
    public decimal OnWaybill { get; set; }

    /// <summary>
    /// Computed using original formula (WQR.CalculateAvailableStock):
    /// AvailableStock = OnStock + Ordered - Reserved - OnWaybill.
    /// </summary>
    public decimal Available => OnStock + Ordered - Reserved - OnWaybill;

    public string Shelf { get; set; } = string.Empty;
    public decimal MinimumStock { get; set; }
    public decimal RequiredStock { get; set; }
}

public class ItemStockPositionResult
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public List<ItemStockPositionRow> Rows { get; set; } = new();

    public decimal TotalOnStock => Rows.Sum(r => r.OnStock);
    public decimal TotalOnTransfer => Rows.Sum(r => r.OnTransfer);
    public decimal TotalReserved => Rows.Sum(r => r.Reserved);
    public decimal TotalOrdered => Rows.Sum(r => r.Ordered);
    public decimal TotalAvailable => Rows.Sum(r => r.Available);
}
