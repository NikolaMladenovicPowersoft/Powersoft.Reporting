using System.Text.Json;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Parses the <c>parametersJson</c> blob saved by each report's schedule modal back into a
/// <see cref="ScheduleParameters"/>. The blob is produced by JavaScript so keys arrive in
/// camelCase; older/code paths may use PascalCase, hence every lookup accepts both.
///
/// Lives in Core (not the Web scheduler) so it can be unit-tested without the web host — the
/// scheduler silently dropping a field here means scheduled reports diverge from the on-screen
/// report, so this path needs regression coverage.
/// </summary>
public static class ScheduleParametersParser
{
    public static ScheduleParameters Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ScheduleParameters();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var p = new ScheduleParameters();

            if (root.TryGetProperty("breakdown", out var bd))
                p.Breakdown = ParseEnum<BreakdownType>(bd);
            else if (root.TryGetProperty("Breakdown", out var bd2))
                p.Breakdown = ParseEnum<BreakdownType>(bd2);

            if (root.TryGetProperty("groupBy", out var gb))
                p.GroupBy = ParseEnum<GroupByType>(gb);
            else if (root.TryGetProperty("GroupBy", out var gb2))
                p.GroupBy = ParseEnum<GroupByType>(gb2);

            if (root.TryGetProperty("secondaryGroupBy", out var sgb))
                p.SecondaryGroupBy = ParseEnum<GroupByType>(sgb);
            else if (root.TryGetProperty("SecondaryGroupBy", out var sgb2))
                p.SecondaryGroupBy = ParseEnum<GroupByType>(sgb2);

            if (root.TryGetProperty("includeVat", out var iv))
                p.IncludeVat = iv.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("compareLastYear", out var cly))
                p.CompareLastYear = cly.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("sortColumn", out var sc))
                p.SortColumn = sc.GetString() ?? "Period";
            if (root.TryGetProperty("sortDirection", out var sd))
                p.SortDirection = sd.GetString() ?? "ASC";

            // storeCodes: can be array ["S1","S2"] or comma-separated string "S1,S2" or empty string
            if (root.TryGetProperty("storeCodes", out var stc) || root.TryGetProperty("StoreCodes", out stc))
                p.StoreCodes = ParseStringList(stc);

            // itemIds: can be array [1,2] or comma-separated string "1,2" or empty string
            if (root.TryGetProperty("itemIds", out var iid) || root.TryGetProperty("ItemIds", out iid))
                p.ItemIds = ParseIntList(iid);

            // PS-specific fields
            if (root.TryGetProperty("reportMode", out var rm) || root.TryGetProperty("ReportMode", out rm))
                p.ReportMode = ParseEnum<PsReportMode>(rm);
            if (root.TryGetProperty("primaryGroup", out var pg) || root.TryGetProperty("PrimaryGroup", out pg))
                p.PrimaryGroup = ParseEnum<PsGroupBy>(pg);
            if (root.TryGetProperty("secondaryGroup", out var sg2p) || root.TryGetProperty("SecondaryGroup", out sg2p))
                p.SecondaryGroup = ParseEnum<PsGroupBy>(sg2p);
            if (root.TryGetProperty("thirdGroup", out var tg) || root.TryGetProperty("ThirdGroup", out tg))
                p.ThirdGroup = ParseEnum<PsGroupBy>(tg);
            if (root.TryGetProperty("showProfit", out var sp))
                p.ShowProfit = sp.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("showStock", out var ss))
                p.ShowStock = ss.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("showOnOrder", out var soo))
                p.ShowOnOrder = soo.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("showReservation", out var sres))
                p.ShowReservation = sres.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("showAvailable", out var sav))
                p.ShowAvailable = sav.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("includeAdditionalCharges", out var iac))
                p.IncludeAdditionalCharges = iac.ValueKind != JsonValueKind.False;

            // ItemsSelection dimension filter (categories/brands/suppliers/stores/items/etc.).
            // The view serialises it as a JSON *string* under "itemsSelectionJson"; older/code
            // paths may embed it as a nested object. Without this the scheduler silently dropped
            // every Items Selection filter and only honoured legacy storeCodes/itemIds.
            if (root.TryGetProperty("itemsSelectionJson", out var isj) || root.TryGetProperty("ItemsSelectionJson", out isj))
            {
                p.ItemsSelectionJson = isj.ValueKind switch
                {
                    JsonValueKind.String => isj.GetString(),
                    JsonValueKind.Object => isj.GetRawText(),
                    _ => null
                };
            }

            // reportDateRange (frontend format) or DateRange (code format)
            if (root.TryGetProperty("reportDateRange", out var rdr) || root.TryGetProperty("DateRange", out rdr))
            {
                if (rdr.ValueKind == JsonValueKind.Object)
                {
                    p.DateRange = new ReportDateRangeOption();
                    if (rdr.TryGetProperty("type", out var t) || rdr.TryGetProperty("Type", out t))
                    {
                        var typeStr = t.GetString();
                        if (Enum.TryParse<ReportDateRangeType>(typeStr, true, out var parsed))
                            p.DateRange.Type = parsed;
                    }
                    if (rdr.TryGetProperty("value", out var v) || rdr.TryGetProperty("Value", out v))
                        p.DateRange.Value = v.TryGetInt32(out var vi) ? vi : 7;
                    if (rdr.TryGetProperty("dateFrom", out var df) || rdr.TryGetProperty("DateFrom", out df))
                        p.DateRange.DateFrom = df.GetString();
                    if (rdr.TryGetProperty("dateTo", out var dt) || rdr.TryGetProperty("DateTo", out dt))
                        p.DateRange.DateTo = dt.GetString();
                }
            }

            return p;
        }
        catch
        {
            return new ScheduleParameters();
        }
    }

    private static T ParseEnum<T>(JsonElement el) where T : struct, Enum
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var num))
            return Enum.IsDefined(typeof(T), num) ? (T)(object)num : default;
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (int.TryParse(s, out var n) && Enum.IsDefined(typeof(T), n))
                return (T)(object)n;
            if (Enum.TryParse<T>(s, true, out var parsed))
                return parsed;
        }
        return default;
    }

    private static List<string>? ParseStringList(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s != "").ToList();
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        return null;
    }

    private static List<int>? ParseIntList(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToList();
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => int.TryParse(v, out _)).Select(v => int.Parse(v)).ToList();
        }
        return null;
    }
}
