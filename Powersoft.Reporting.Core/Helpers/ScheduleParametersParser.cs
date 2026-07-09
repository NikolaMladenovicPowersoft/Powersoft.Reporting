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

            // Pareto 80/20 specific (keys produced by collectParetoParams() in Pareto.cshtml)
            if (root.TryGetProperty("dimension", out var pd) || root.TryGetProperty("ParetoDimension", out pd))
                p.ParetoDimension = pd.GetString();
            if (root.TryGetProperty("metric", out var pm) || root.TryGetProperty("ParetoMetric", out pm))
                p.ParetoMetric = pm.GetString();
            if (root.TryGetProperty("excludeNegativeAmounts", out var ena))
                p.ExcludeNegativeAmounts = ena.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("classAThreshold", out var cat) || root.TryGetProperty("ClassAThreshold", out cat))
                p.ClassAThreshold = ParseDecimal(cat, 80);
            if (root.TryGetProperty("classBThreshold", out var cbt) || root.TryGetProperty("ClassBThreshold", out cbt))
                p.ClassBThreshold = ParseDecimal(cbt, 95);
            if (root.TryGetProperty("profitBasis", out var pb) || root.TryGetProperty("ProfitBasis", out pb))
                p.ProfitBasis = (int)ParseEnum<ParetoProfitBasis>(pb);
            if (root.TryGetProperty("priceInterval", out var pi) || root.TryGetProperty("PriceInterval", out pi))
                p.PriceInterval = ParseDecimal(pi, 10);
            if (root.TryGetProperty("priceOnIndex", out var poi) || root.TryGetProperty("PriceOnIndex", out poi))
                p.PriceOnIndex = ParseInt(poi, 0);
            if (root.TryGetProperty("priceOnIncludesVat", out var poiv))
                p.PriceOnIncludesVat = poiv.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("timezoneOffsetMinutes", out var tzo) || root.TryGetProperty("TimezoneOffsetMinutes", out tzo))
                p.TimezoneOffsetMinutes = ParseInt(tzo, 0);

            // Charts specific (keys produced by collectChartParams() in Charts.cshtml).
            // showOthers/compareLastYear arrive as "1"/"0" strings here, hence ParseBool.
            if (root.TryGetProperty("mode", out var cmode) || root.TryGetProperty("ChartMode", out cmode))
                p.ChartMode = cmode.GetString();
            if (root.TryGetProperty("chartType", out var ctype) || root.TryGetProperty("ChartType", out ctype))
                p.ChartType = ctype.GetString();
            if (root.TryGetProperty("topN", out var ctn) || root.TryGetProperty("TopN", out ctn))
                p.TopN = ParseInt(ctn, 10);
            if (root.TryGetProperty("showOthers", out var cso) || root.TryGetProperty("ShowOthers", out cso))
                p.ShowOthers = ParseBool(cso, true);
            // dimension/metric keys are shared with Pareto; capture them into chart fields too.
            if (root.TryGetProperty("dimension", out var cdim))
                p.ChartDimension = cdim.GetString();
            if (root.TryGetProperty("metric", out var cmet))
                p.ChartMetric = cmet.GetString();
            // compareLastYear may be "1"/"0" (charts) or true/false (AB) — re-read defensively.
            if (root.TryGetProperty("compareLastYear", out var cclyy))
                p.CompareLastYear = ParseBool(cclyy, p.CompareLastYear);

            // CancelLog specific (keys from collectScheduleParameters() in CancelLog.cshtml)
            if (root.TryGetProperty("actionType", out var clat) || root.TryGetProperty("ActionType", out clat))
                p.CancelActionType = clat.GetString();
            if (root.TryGetProperty("clReportType", out var clrt) || root.TryGetProperty("CancelLogReportType", out clrt))
                p.CancelLogReportType = clrt.GetString();
            if (root.TryGetProperty("clPrimaryGroup", out var clpg) || root.TryGetProperty("CancelLogPrimaryGroup", out clpg))
                p.CancelLogPrimaryGroup = clpg.GetString();
            if (root.TryGetProperty("clSecondaryGroup", out var clsg) || root.TryGetProperty("CancelLogSecondaryGroup", out clsg))
                p.CancelLogSecondaryGroup = clsg.GetString();
            if (root.TryGetProperty("reportByDateTime", out var clrbdt))
                p.ReportByDateTime = ParseBool(clrbdt, false);
            if (root.TryGetProperty("maxRecords", out var clmr) || root.TryGetProperty("MaxRecords", out clmr))
                p.MaxRecords = ParseInt(clmr, 50000);

            // Trial Balance specific (keys from collectScheduleParameters() in TrialBalance.cshtml)
            if (root.TryGetProperty("tbReportMode", out var tbrm) || root.TryGetProperty("TbReportMode", out tbrm))
                p.TbReportMode = AsString(tbrm);
            if (root.TryGetProperty("tbIncludeZeroMovements", out var tbiz) || root.TryGetProperty("TbIncludeZeroMovements", out tbiz))
                p.TbIncludeZeroMovements = ParseBool(tbiz, false);
            if (root.TryGetProperty("tbSelectedAccounts", out var tbsa) || root.TryGetProperty("TbSelectedAccounts", out tbsa))
                p.TbSelectedAccounts = AsString(tbsa);
            if (root.TryGetProperty("tbSelectedHeaders", out var tbsh) || root.TryGetProperty("TbSelectedHeaders", out tbsh))
                p.TbSelectedHeaders = AsString(tbsh);
            if (root.TryGetProperty("tbSuppressedHeaders", out var tbsup) || root.TryGetProperty("TbSuppressedHeaders", out tbsup))
                p.TbSuppressedHeaders = AsString(tbsup);

            // Profit & Loss specific (keys from collectProfitLossParams() in ProfitLoss.cshtml).
            if (root.TryGetProperty("headerLevel", out var plhl) || root.TryGetProperty("PlHeaderLevel", out plhl))
                p.PlHeaderLevel = ParseBool(plhl, false);
            if (root.TryGetProperty("compareToLastYear", out var plcmp) || root.TryGetProperty("PlCompareToLastYear", out plcmp))
                p.PlCompareToLastYear = ParseBool(plcmp, false);
            if (root.TryGetProperty("openingStockValue", out var plos) || root.TryGetProperty("PlOpeningStockValue", out plos))
                p.PlOpeningStockValue = ParseDecimal(plos, 0);
            if (root.TryGetProperty("closingStockValue", out var plcs) || root.TryGetProperty("PlClosingStockValue", out plcs))
                p.PlClosingStockValue = ParseDecimal(plcs, 0);
            if (root.TryGetProperty("suppressedHeaders", out var plsup) || root.TryGetProperty("PlSuppressedHeaders", out plsup))
                p.PlSuppressedHeaders = AsString(plsup);

            // Cash Flow specific (keys from collectScheduleParameters() in CashFlow.cshtml).
            if (root.TryGetProperty("cfShowAccounts", out var cfsa) || root.TryGetProperty("CfShowAccounts", out cfsa))
                p.CfShowAccounts = ParseBool(cfsa, false);
            if (root.TryGetProperty("cfCompareToLastYear", out var cfcmp) || root.TryGetProperty("CfCompareToLastYear", out cfcmp))
                p.CfCompareToLastYear = ParseBool(cfcmp, false);
            if (root.TryGetProperty("cfIncludeBudget", out var cfbud) || root.TryGetProperty("CfIncludeBudget", out cfbud))
                p.CfIncludeBudget = ParseBool(cfbud, false);
            if (root.TryGetProperty("cfMonthly", out var cfmo) || root.TryGetProperty("CfMonthly", out cfmo))
                p.CfMonthly = ParseBool(cfmo, false);

            // Catalogue specific (keys from collectCatalogueParams() in Catalogue.cshtml).
            // reportMode/primaryGroup/etc. share key names with PS but carry CatalogueGroupBy
            // values; captured into distinct Cat* fields so the Catalogue handler reads the
            // right enum without clobbering the PS fields above.
            if (root.TryGetProperty("reportMode", out var catRm) || root.TryGetProperty("CatReportMode", out catRm))
                p.CatReportMode = AsString(catRm);
            if (root.TryGetProperty("reportOn", out var catRo) || root.TryGetProperty("CatReportOn", out catRo))
                p.CatReportOn = AsString(catRo);
            if (root.TryGetProperty("primaryGroup", out var catPg) || root.TryGetProperty("CatPrimaryGroup", out catPg))
                p.CatPrimaryGroup = AsString(catPg);
            if (root.TryGetProperty("secondaryGroup", out var catSg) || root.TryGetProperty("CatSecondaryGroup", out catSg))
                p.CatSecondaryGroup = AsString(catSg);
            if (root.TryGetProperty("thirdGroup", out var catTg) || root.TryGetProperty("CatThirdGroup", out catTg))
                p.CatThirdGroup = AsString(catTg);
            if (root.TryGetProperty("displayColumns", out var catDc) || root.TryGetProperty("CatDisplayColumns", out catDc))
                p.CatDisplayColumns = AsString(catDc);
            if (root.TryGetProperty("dateBasis", out var catDb) || root.TryGetProperty("CatDateBasis", out catDb))
                p.CatDateBasis = AsString(catDb);
            if (root.TryGetProperty("useDateTime", out var catUdt) || root.TryGetProperty("CatUseDateTime", out catUdt))
                p.CatUseDateTime = ParseBool(catUdt, false);
            if (root.TryGetProperty("profitBasedOn", out var catPbo) || root.TryGetProperty("CatProfitBasedOn", out catPbo))
                p.CatProfitBasedOn = ParseInt(catPbo, 99);
            if (root.TryGetProperty("profitIncludesVat", out var catPiv) || root.TryGetProperty("CatProfitIncludesVat", out catPiv))
                p.CatProfitIncludesVat = ParseBool(catPiv, false);
            if (root.TryGetProperty("stockValueBasedOn", out var catSvb) || root.TryGetProperty("CatStockValueBasedOn", out catSvb))
                p.CatStockValueBasedOn = ParseInt(catSvb, 99);
            if (root.TryGetProperty("stockValueIncludesVat", out var catSiv) || root.TryGetProperty("CatStockValueIncludesVat", out catSiv))
                p.CatStockValueIncludesVat = ParseBool(catSiv, false);
            if (root.TryGetProperty("costType", out var catCt) || root.TryGetProperty("CatCostType", out catCt))
                p.CatCostType = ParseInt(catCt, 99);
            if (root.TryGetProperty("columnFilters", out var catCf) || root.TryGetProperty("CatColumnFilters", out catCf))
                p.CatColumnFilters = AsString(catCf);

            // ProspectClients specific (prefix Pc to avoid clashing with shared fields)
            if (root.TryGetProperty("dateField",        out var pcDf)  || root.TryGetProperty("PcDateField",        out pcDf))  p.PcDateField        = AsString(pcDf);
            if (root.TryGetProperty("statusFilter",     out var pcSf)  || root.TryGetProperty("PcStatusFilter",     out pcSf))  p.PcStatusFilter     = AsString(pcSf);
            if (root.TryGetProperty("priorityFilter",   out var pcPf)  || root.TryGetProperty("PcPriorityFilter",   out pcPf))  p.PcPriorityFilter   = AsString(pcPf);
            if (root.TryGetProperty("followedByFilter", out var pcFb)  || root.TryGetProperty("PcFollowedByFilter", out pcFb))  p.PcFollowedByFilter = AsString(pcFb);
            if (root.TryGetProperty("category1Filter",  out var pcC1)  || root.TryGetProperty("PcCategory1Filter",  out pcC1))  p.PcCategory1Filter  = AsString(pcC1);
            if (root.TryGetProperty("category2Filter",  out var pcC2)  || root.TryGetProperty("PcCategory2Filter",  out pcC2))  p.PcCategory2Filter  = AsString(pcC2);
            if (root.TryGetProperty("primaryGroup",     out var pcPg)  || root.TryGetProperty("PcPrimaryGroup",     out pcPg))  p.PcPrimaryGroup     = AsString(pcPg);
            if (root.TryGetProperty("secondaryGroup",   out var pcSg)  || root.TryGetProperty("PcSecondaryGroup",   out pcSg))  p.PcSecondaryGroup   = AsString(pcSg);
            if (root.TryGetProperty("includeHistory",   out var pcIh)  || root.TryGetProperty("PcIncludeHistory",   out pcIh))  p.PcIncludeHistory   = ParseBool(pcIh, false);
            if (root.TryGetProperty("customerCodesJson", out var pcCcj) || root.TryGetProperty("PcCustomerCodesJson", out pcCcj)) p.PcCustomerCodesJson = AsString(pcCcj);
            if (root.TryGetProperty("customerExcludeMode", out var pcCem) || root.TryGetProperty("PcCustomerExcludeMode", out pcCem)) p.PcCustomerExcludeMode = ParseBool(pcCem, false);

            // OffersReport specific (prefix Or)
            if (root.TryGetProperty("dateField",    out var orDf) || root.TryGetProperty("OrDateField",    out orDf)) p.OrDateField    = AsString(orDf);
            if (root.TryGetProperty("statusFilter", out var orSf) || root.TryGetProperty("OrStatusFilter", out orSf)) p.OrStatusFilter = AsString(orSf);
            if (root.TryGetProperty("storeFilter",  out var orStf)|| root.TryGetProperty("OrStoreFilter",  out orStf))p.OrStoreFilter  = AsString(orStf);
            if (root.TryGetProperty("agentFilter",  out var orAf) || root.TryGetProperty("OrAgentFilter",  out orAf)) p.OrAgentFilter  = AsString(orAf);
            if (root.TryGetProperty("primaryGroup", out var orPg) || root.TryGetProperty("OrPrimaryGroup", out orPg)) p.OrPrimaryGroup = AsString(orPg);
            if (root.TryGetProperty("secondaryGroup",out var orSg)|| root.TryGetProperty("OrSecondaryGroup",out orSg))p.OrSecondaryGroup = AsString(orSg);
            if (root.TryGetProperty("thirdGroup",   out var orTg) || root.TryGetProperty("OrThirdGroup",   out orTg)) p.OrThirdGroup   = AsString(orTg);
            if (root.TryGetProperty("offerType",    out var orOt) || root.TryGetProperty("OrOfferType",    out orOt)) p.OrOfferType    = AsString(orOt);
            if (root.TryGetProperty("includeHistory",out var orIh)|| root.TryGetProperty("OrIncludeHistory",out orIh))p.OrIncludeHistory = ParseBool(orIh, false);
            if (root.TryGetProperty("customerCodesJson", out var orCcj) || root.TryGetProperty("OrCustomerCodesJson", out orCcj)) p.OrCustomerCodesJson = AsString(orCcj);
            if (root.TryGetProperty("customerExcludeMode", out var orCem) || root.TryGetProperty("OrCustomerExcludeMode", out orCem)) p.OrCustomerExcludeMode = ParseBool(orCem, false);
            if (root.TryGetProperty("statusCodesJson", out var orScj) || root.TryGetProperty("OrStatusCodesJson", out orScj)) p.OrStatusCodesJson = AsString(orScj);
            if (root.TryGetProperty("storeCodesJson",  out var orStcj)|| root.TryGetProperty("OrStoreCodesJson",  out orStcj))p.OrStoreCodesJson  = AsString(orStcj);
            if (root.TryGetProperty("agentCodesJson",  out var orAcj) || root.TryGetProperty("OrAgentCodesJson",  out orAcj)) p.OrAgentCodesJson  = AsString(orAcj);

            // Items Not Purchased specific (keys from collectScheduleParameters() in CustomerNotPurchased.cshtml).
            // Distinct cnp* keys avoid clashing with AverageBasket "groupBy" and generic "days".
            if (root.TryGetProperty("cnpDays", out var cnpD) || root.TryGetProperty("CnpDaysThreshold", out cnpD))
                p.CnpDaysThreshold = ParseInt(cnpD, 30);
            if (root.TryGetProperty("cnpGroupBy", out var cnpGb) || root.TryGetProperty("CnpGroupBy", out cnpGb))
                p.CnpGroupBy = AsString(cnpGb);
            if (root.TryGetProperty("cnpIncludeNeverPurchased", out var cnpInp) || root.TryGetProperty("CnpIncludeNeverPurchased", out cnpInp))
                p.CnpIncludeNeverPurchased = ParseBool(cnpInp, false);
            if (root.TryGetProperty("cnpCustomerCodesJson", out var cnpCcj) || root.TryGetProperty("CnpCustomerCodesJson", out cnpCcj))
                p.CnpCustomerCodesJson = AsString(cnpCcj);
            if (root.TryGetProperty("cnpCustomerExcludeMode", out var cnpCem) || root.TryGetProperty("CnpCustomerExcludeMode", out cnpCem))
                p.CnpCustomerExcludeMode = ParseBool(cnpCem, false);

            // ItemsSelection dimension filter (categories/brands/suppliers/stores/items/etc.).
            // The view serialises it as a JSON *string* under "itemsSelectionJson"; older/code
            // paths may embed it as a nested object. Without this the scheduler silently dropped
            // every Items Selection filter and only honoured legacy storeCodes/itemIds.
            if (root.TryGetProperty("itemsSelectionJson", out var isj) || root.TryGetProperty("ItemsSelectionJson", out isj)
                || root.TryGetProperty("itemsSelection", out isj) || root.TryGetProperty("ItemsSelection", out isj))
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

            // Fallback: screen dateFrom/dateTo at root (CancelLog collectScheduleParameters snapshot).
            if (p.DateRange == null
                && (root.TryGetProperty("dateFrom", out var rootDf) || root.TryGetProperty("DateFrom", out rootDf))
                && (root.TryGetProperty("dateTo", out var rootDt) || root.TryGetProperty("DateTo", out rootDt)))
            {
                p.DateRange = new ReportDateRangeOption
                {
                    Type = ReportDateRangeType.Custom,
                    DateFrom = rootDf.GetString(),
                    DateTo = rootDt.GetString()
                };
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

    private static string? AsString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private static bool ParseBool(JsonElement el, bool fallback)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return el.TryGetInt32(out var n) ? n != 0 : fallback;
            case JsonValueKind.String:
                var s = el.GetString();
                if (string.IsNullOrEmpty(s)) return fallback;
                if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                return fallback;
            default: return fallback;
        }
    }

    private static decimal ParseDecimal(JsonElement el, decimal fallback)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
        return fallback;
    }

    private static int ParseInt(JsonElement el, int fallback)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var ns)) return ns;
        return fallback;
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
