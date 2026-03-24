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
        string Customer = "");

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

        return (sb.ToString(), parms);
    }

    internal static bool NeedsItemJoin(ItemsSelectionFilter? sel)
    {
        if (sel == null) return false;
        return sel.Categories.HasFilter || sel.Departments.HasFilter
            || sel.Brands.HasFilter || sel.Seasons.HasFilter;
    }

    private static void AppendFilter(
        StringBuilder sb, List<SqlParameter> parms,
        DimensionFilter filter, string column, string prefix, ref int idx)
    {
        if (!filter.HasFilter) return;

        var op = filter.Mode == FilterMode.Exclude ? "NOT IN" : "IN";
        var names = new List<string>();
        foreach (var id in filter.Ids)
        {
            var p = $"@{prefix}{idx++}";
            names.Add(p);
            parms.Add(new SqlParameter(p, id));
        }
        sb.Append($" AND {column} {op} ({string.Join(",", names)})");
    }
}
