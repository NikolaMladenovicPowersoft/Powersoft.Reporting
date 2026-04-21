using System.Text;
using Microsoft.Data.SqlClient;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Data.Tenant;

internal static class DimensionFilterBuilder
{
    internal record ColumnMap(
        string Category = "t2.fk_CategoryID",
        string Department = "t2.fk_DepartmentID",
        string Brand = "t2.fk_BrandID",
        string Season = "t2.fk_SeasonID",
        string Item = "t2.pk_ItemID",
        string Store = "t1.fk_StoreCode",
        string Supplier = "",
        string Customer = "",
        string Agent = "",
        string PostalCode = "",
        // User column applies to BOTH legs (every header has fk_UserCode).
        string User = "",
        string ItemTableAlias = "t2");

    internal static readonly ColumnMap Default = new();

    internal static (string whereSql, List<SqlParameter> parameters) Build(
        ItemsSelectionFilter? sel, int startIdx = 0) =>
        Build(sel, Default, startIdx);

    internal static (string whereSql, List<SqlParameter> parameters) Build(
        ItemsSelectionFilter? sel, ColumnMap cols, int startIdx = 0)
    {
        if (sel == null) return ("", new List<SqlParameter>());

        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();
        int idx = startIdx;

        if (!string.IsNullOrEmpty(cols.Category))
            AppendFilter(sb, parms, sel.Categories, cols.Category, "cat", ref idx);
        if (!string.IsNullOrEmpty(cols.Department))
            AppendFilter(sb, parms, sel.Departments, cols.Department, "dep", ref idx);
        if (!string.IsNullOrEmpty(cols.Brand))
            AppendFilter(sb, parms, sel.Brands, cols.Brand, "br", ref idx);
        if (!string.IsNullOrEmpty(cols.Season))
            AppendFilter(sb, parms, sel.Seasons, cols.Season, "sea", ref idx);
        if (!string.IsNullOrEmpty(cols.Item))
            AppendFilter(sb, parms, sel.Items, cols.Item, "itm", ref idx);
        if (!string.IsNullOrEmpty(cols.Store))
            AppendFilter(sb, parms, sel.Stores, cols.Store, "sto", ref idx);
        if (!string.IsNullOrEmpty(cols.Supplier))
            AppendFilter(sb, parms, sel.Suppliers, cols.Supplier, "sup", ref idx);
        if (!string.IsNullOrEmpty(cols.Customer))
            AppendFilter(sb, parms, sel.Customers, cols.Customer, "cus", ref idx);
        if (!string.IsNullOrEmpty(cols.Agent))
            AppendFilter(sb, parms, sel.Agents, cols.Agent, "agn", ref idx);
        if (!string.IsNullOrEmpty(cols.PostalCode))
            AppendFilter(sb, parms, sel.PostalCodes, cols.PostalCode, "pcd", ref idx);
        if (!string.IsNullOrEmpty(cols.User))
            AppendFilter(sb, parms, sel.Users, cols.User, "usr", ref idx);

        AppendPropertyFilters(sb, parms, sel, cols.ItemTableAlias, ref idx);

        return (sb.ToString(), parms);
    }

    /// <summary>
    /// Builds WHERE fragment for SALE-LEG only filters: Customer, Agent, PostalCode,
    /// PaymentType, ZReport, Town. Returns empty if no sale-only filter is active.
    /// Uses prefix 'so' so params don't clash with general Build().
    /// Caller is responsible for appending the fragment only to sale-leg SQL (invoice/credit note headers).
    /// Mirrors legacy repPowerReportCatalogue.aspx.vb:3760-3795 behaviour.
    /// </summary>
    internal static (string whereSql, List<SqlParameter> parameters) BuildSaleOnly(
        ItemsSelectionFilter? sel,
        string customerColumn, string agentColumn, string postalCodeColumn,
        string paymentTypeColumn = "", string zReportColumn = "", string townColumn = "",
        int startIdx = 0)
    {
        if (sel == null) return ("", new List<SqlParameter>());

        var sb = new StringBuilder();
        var parms = new List<SqlParameter>();
        int idx = startIdx;

        if (!string.IsNullOrEmpty(customerColumn))
            AppendFilter(sb, parms, sel.Customers, customerColumn, "socus", ref idx);
        if (!string.IsNullOrEmpty(agentColumn))
            AppendFilter(sb, parms, sel.Agents, agentColumn, "soagn", ref idx);
        if (!string.IsNullOrEmpty(postalCodeColumn))
            AppendFilter(sb, parms, sel.PostalCodes, postalCodeColumn, "sopcd", ref idx);
        if (!string.IsNullOrEmpty(paymentTypeColumn))
            AppendFilter(sb, parms, sel.PaymentTypes, paymentTypeColumn, "sopt", ref idx);
        if (!string.IsNullOrEmpty(zReportColumn))
            AppendFilter(sb, parms, sel.ZReports, zReportColumn, "sozr", ref idx);
        if (!string.IsNullOrEmpty(townColumn))
            AppendFilter(sb, parms, sel.Towns, townColumn, "sotwn", ref idx);

        return (sb.ToString(), parms);
    }

    internal static bool HasSaleOnlyFilters(ItemsSelectionFilter? sel)
    {
        if (sel == null) return false;
        return sel.Customers.HasFilter || sel.Agents.HasFilter || sel.PostalCodes.HasFilter
            || sel.PaymentTypes.HasFilter || sel.ZReports.HasFilter || sel.Towns.HasFilter;
    }

    internal static bool NeedsItemJoin(ItemsSelectionFilter? sel)
    {
        if (sel == null) return false;
        return sel.Categories.HasFilter || sel.Departments.HasFilter
            || sel.Brands.HasFilter || sel.Seasons.HasFilter
            || sel.HasPropertyFilters;
    }

    private static void AppendPropertyFilters(
        StringBuilder sb, List<SqlParameter> parms,
        ItemsSelectionFilter sel, string alias, ref int idx)
    {
        if (sel.Stock == StockFilter.WithStock)
            sb.Append($" AND ISNULL({alias}.TotalStockQty, 0) > 0");
        else if (sel.Stock == StockFilter.WithoutStock)
            sb.Append($" AND ISNULL({alias}.TotalStockQty, 0) = 0");

        if (sel.ECommerceOnly == true)
            sb.Append($" AND ISNULL({alias}.ECommerce, 0) = 1");

        if (sel.ModifiedAfter.HasValue)
        {
            var p = $"@pModAfter{idx++}";
            sb.Append($" AND {alias}.LastModifiedDate >= {p}");
            parms.Add(new SqlParameter(p, sel.ModifiedAfter.Value));
        }

        if (sel.CreatedAfter.HasValue)
        {
            var p = $"@pCreAfter{idx++}";
            sb.Append($" AND {alias}.CreationDate >= {p}");
            parms.Add(new SqlParameter(p, sel.CreatedAfter.Value));
        }

        if (sel.ReleasedAfter.HasValue)
        {
            var p = $"@pRelAfter{idx++}";
            sb.Append($" AND {alias}.ReleaseDate >= {p}");
            parms.Add(new SqlParameter(p, sel.ReleasedAfter.Value));
        }
    }

    private const string NaMarker = "__NA__";

    private static void AppendFilter(
        StringBuilder sb, List<SqlParameter> parms,
        DimensionFilter filter, string column, string prefix, ref int idx)
    {
        if (!filter.HasFilter) return;

        bool hasNa = filter.Ids.Contains(NaMarker);
        var realIds = filter.Ids.Where(id => id != NaMarker).ToList();

        if (filter.Mode == FilterMode.Include)
        {
            if (realIds.Count > 0 && hasNa)
            {
                var names = AddParams(parms, realIds, prefix, ref idx);
                sb.Append($" AND ({column} IN ({string.Join(",", names)}) OR {column} IS NULL)");
            }
            else if (realIds.Count > 0)
            {
                var names = AddParams(parms, realIds, prefix, ref idx);
                sb.Append($" AND {column} IN ({string.Join(",", names)})");
            }
            else if (hasNa)
            {
                sb.Append($" AND {column} IS NULL");
            }
        }
        else // Exclude
        {
            if (realIds.Count > 0 && hasNa)
            {
                var names = AddParams(parms, realIds, prefix, ref idx);
                sb.Append($" AND ({column} NOT IN ({string.Join(",", names)}) AND {column} IS NOT NULL)");
            }
            else if (realIds.Count > 0)
            {
                var names = AddParams(parms, realIds, prefix, ref idx);
                sb.Append($" AND {column} NOT IN ({string.Join(",", names)})");
            }
            else if (hasNa)
            {
                sb.Append($" AND {column} IS NOT NULL");
            }
        }
    }

    private static List<string> AddParams(
        List<SqlParameter> parms, List<string> ids, string prefix, ref int idx)
    {
        var names = new List<string>();
        foreach (var id in ids)
        {
            var p = $"@{prefix}{idx++}";
            names.Add(p);
            parms.Add(new SqlParameter(p, id));
        }
        return names;
    }
}
