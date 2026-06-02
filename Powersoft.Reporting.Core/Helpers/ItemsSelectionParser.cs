using System.Text.Json;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Parses the JSON produced by _ItemsSelection.cshtml's getItemsSelectionFilter()
/// into an <see cref="ItemsSelectionFilter"/>.  Shared between ReportsController
/// and ScheduleExecutionService so scheduled reports honour the same dimension filters.
/// </summary>
public static class ItemsSelectionParser
{
    public static ItemsSelectionFilter? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var filter = new ItemsSelectionFilter();

            ParseDimension(root, "categories", filter.Categories);
            ParseDimension(root, "departments", filter.Departments);
            ParseDimension(root, "brands", filter.Brands);
            ParseDimension(root, "seasons", filter.Seasons);
            ParseDimension(root, "suppliers", filter.Suppliers);
            ParseDimension(root, "customers", filter.Customers);
            ParseDimension(root, "agents", filter.Agents);
            ParseDimension(root, "postalcodes", filter.PostalCodes);
            ParseDimension(root, "paymenttypes", filter.PaymentTypes);
            ParseDimension(root, "zreports", filter.ZReports);
            ParseDimension(root, "towns", filter.Towns);
            ParseDimension(root, "users", filter.Users);
            ParseDimension(root, "stores", filter.Stores);
            ParseDimension(root, "items", filter.Items);

            ParseDimension(root, "models", filter.Models);
            ParseDimension(root, "colours", filter.Colours);
            ParseDimension(root, "sizes", filter.Sizes);
            ParseDimension(root, "groupSizes", filter.GroupSizes);
            ParseDimension(root, "fabrics", filter.Fabrics);
            ParseDimension(root, "attributes1", filter.Attributes1);
            ParseDimension(root, "attributes2", filter.Attributes2);
            ParseDimension(root, "attributes3", filter.Attributes3);
            ParseDimension(root, "attributes4", filter.Attributes4);
            ParseDimension(root, "attributes5", filter.Attributes5);
            ParseDimension(root, "attributes6", filter.Attributes6);

            if (root.TryGetProperty("stock", out var stockEl))
            {
                var sv = stockEl.GetString() ?? "all";
                filter.Stock = sv switch
                {
                    "withStock" => StockFilter.WithStock,
                    "withoutStock" => StockFilter.WithoutStock,
                    _ => StockFilter.All
                };
            }

            if (root.TryGetProperty("ecommerceOnly", out var ecomEl) && ecomEl.GetBoolean())
                filter.ECommerceOnly = true;

            if (root.TryGetProperty("modifiedAfter", out var modEl) && modEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(modEl.GetString(), out var dt)) filter.ModifiedAfter = dt;
            }
            if (root.TryGetProperty("modifiedBefore", out var modBEl) && modBEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(modBEl.GetString(), out var dt)) filter.ModifiedBefore = dt;
            }

            if (root.TryGetProperty("createdAfter", out var creEl) && creEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(creEl.GetString(), out var dt)) filter.CreatedAfter = dt;
            }
            if (root.TryGetProperty("createdBefore", out var creBEl) && creBEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(creBEl.GetString(), out var dt)) filter.CreatedBefore = dt;
            }

            if (root.TryGetProperty("releasedAfter", out var relEl) && relEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(relEl.GetString(), out var dt)) filter.ReleasedAfter = dt;
            }
            if (root.TryGetProperty("releasedBefore", out var relBEl) && relBEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(relBEl.GetString(), out var dt)) filter.ReleasedBefore = dt;
            }

            return filter;
        }
        catch
        {
            return null;
        }
    }

    private static void ParseDimension(JsonElement root, string key, DimensionFilter target)
    {
        if (!root.TryGetProperty(key, out var el)) return;
        if (el.TryGetProperty("mode", out var modeEl))
        {
            if (modeEl.ValueKind == JsonValueKind.Number)
            {
                target.Mode = modeEl.GetInt32() switch
                {
                    1 => FilterMode.Include,
                    2 => FilterMode.Exclude,
                    _ => FilterMode.All
                };
            }
            else
            {
                var modeStr = modeEl.GetString() ?? "all";
                target.Mode = modeStr switch
                {
                    "include" => FilterMode.Include,
                    "exclude" => FilterMode.Exclude,
                    _ => FilterMode.All
                };
            }
        }
        if (el.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            target.Ids = idsEl.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32().ToString() : e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
