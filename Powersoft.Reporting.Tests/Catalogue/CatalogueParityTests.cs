using System.Reflection;
using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Tenant;
using Xunit;

namespace Powersoft.Reporting.Tests.Catalogue;

/// <summary>
/// Locks in legacy-parity rules for the Power Reports Catalogue SQL builder.
/// These tests do NOT touch a database. They invoke the private SQL-building
/// methods via reflection and assert structural properties of the generated SQL.
///
/// If any of these tests fail, you have changed legacy-parity behaviour.
/// Stop and discuss with Nikola before "fixing" the test.
/// </summary>
public class CatalogueParityTests
{
    private const string DummyConnString = "Server=.;Database=fake;Trusted_Connection=True;";

    private static string InvokeBuildUnionAll(CatalogueFilter filter)
    {
        var repo = new CatalogueRepository(DummyConnString);
        var t = typeof(CatalogueRepository);

        var buildGrouping = t.GetMethod("BuildGrouping", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildGrouping not found");
        var buildItemFilters = t.GetMethod("BuildItemFilters", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildItemFilters not found");
        var buildUnionAll = t.GetMethod("BuildUnionAll", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildUnionAll not found");

        var grouping = buildGrouping.Invoke(repo, new object[] { filter })!;
        var itemFilters = buildItemFilters.Invoke(repo, new object[] { filter })!;
        return (string)buildUnionAll.Invoke(repo, new[] { filter, grouping, itemFilters })!;
    }

    private static CatalogueFilter MakeFilter(CatalogueReportOn reportOn = CatalogueReportOn.Both,
        CatalogueReportMode mode = CatalogueReportMode.Detailed)
    {
        return new CatalogueFilter
        {
            DateFrom = new DateTime(2025, 1, 1),
            DateTo = new DateTime(2025, 12, 31),
            ReportOn = reportOn,
            ReportMode = mode,
            PrimaryGroup = CatalogueGroupBy.None,
            SecondaryGroup = CatalogueGroupBy.None,
            ThirdGroup = CatalogueGroupBy.None,
            PageNumber = 1,
            PageSize = 50
        };
    }

    // --------------------------------------------------------------------------
    // FACT 1: All 4 transaction tables appear when ReportOn=Both
    // --------------------------------------------------------------------------
    [Fact]
    public void When_ReportOnBoth_AllFourTransactionTablesAppear()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both));

        sql.Should().Contain("tbl_InvoiceDetails", because: "sales positive leg");
        sql.Should().Contain("tbl_CreditDetails", because: "sales return/credit leg");
        sql.Should().Contain("tbl_PurchInvoiceDetails", because: "purchase positive leg");
        sql.Should().Contain("tbl_PurchReturnDetails", because: "purchase return leg");
    }

    [Fact]
    public void When_ReportOnSale_OnlySaleTablesAppear()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale));

        sql.Should().Contain("tbl_InvoiceDetails");
        sql.Should().Contain("tbl_CreditDetails");
        sql.Should().NotContain("tbl_PurchInvoiceDetails");
        sql.Should().NotContain("tbl_PurchReturnDetails");
    }

    [Fact]
    public void When_ReportOnPurchase_OnlyPurchaseTablesAppear()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Purchase));

        sql.Should().NotContain("tbl_InvoiceDetails");
        sql.Should().NotContain("tbl_CreditDetails");
        sql.Should().Contain("tbl_PurchInvoiceDetails");
        sql.Should().Contain("tbl_PurchReturnDetails");
    }

    // --------------------------------------------------------------------------
    // FACT 2: Returns/credits financial columns are negated; stock is NOT
    // (mirrors RAS_PowerReportCatalogueOnSale.txt lines 362–441)
    // --------------------------------------------------------------------------
    [Fact]
    public void Credit_Leg_Negates_Quantity_AndFinancials()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale));
        var creditLeg = ExtractLeg(sql, "tbl_CreditDetails");

        creditLeg.Should().MatchRegex(@"\(-1\)\s*\*.*?Quantity",
            because: "legacy negates Quantity for Credit (return) leg");
        creditLeg.Should().Contain("(-1)", because: "credit leg must apply sign reversal somewhere");
    }

    [Fact]
    public void PurchaseReturn_Leg_Negates_Quantity_AndFinancials()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Purchase));
        var returnLeg = ExtractLeg(sql, "tbl_PurchReturnDetails");

        returnLeg.Should().Contain("(-1)",
            because: "legacy negates financials for PurchReturn leg");
    }

    // --------------------------------------------------------------------------
    // FACT 3: Positive legs do NOT negate
    // --------------------------------------------------------------------------
    [Fact]
    public void Invoice_Leg_DoesNot_Negate_Quantity()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale));
        var invoiceLeg = ExtractLeg(sql, "tbl_InvoiceDetails", upToTable: "tbl_CreditDetails");

        // Invoice leg should not contain (-1) negation patterns on its main columns
        invoiceLeg.Should().NotMatchRegex(@"\(-1\)\s*\*.*?As\s+Quantity",
            because: "positive sale leg keeps native sign");
    }

    [Fact]
    public void PurchInvoice_Leg_DoesNot_Negate_Quantity()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Purchase));
        var purchLeg = ExtractLeg(sql, "tbl_PurchInvoiceDetails", upToTable: "tbl_PurchReturnDetails");

        purchLeg.Should().NotMatchRegex(@"\(-1\)\s*\*.*?As\s+Quantity",
            because: "positive purchase leg keeps native sign");
    }

    // --------------------------------------------------------------------------
    // FACT 4: Invoice Type codes match legacy: I, C, P, E
    // --------------------------------------------------------------------------
    [Fact]
    public void InvoiceType_Codes_Match_Legacy()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both));

        sql.Should().Contain("'I'", because: "Invoice (sale)");
        sql.Should().Contain("'C'", because: "Credit (sale return)");
        sql.Should().Contain("'P'", because: "Purchase Invoice");
        sql.Should().Contain("'E'", because: "Purchase Return (legacy code 'E')");
    }

    // --------------------------------------------------------------------------
    // FACT 5: 4 UNION ALLs when ReportOn=Both (3 separators between 4 legs)
    // --------------------------------------------------------------------------
    [Fact]
    public void Both_ReportOn_Produces_Three_UnionAll_Separators()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both));
        var unionCount = CountOccurrences(sql, "UNION ALL");

        unionCount.Should().Be(3, because: "4 legs joined by 3 UNION ALLs");
    }

    [Fact]
    public void Sale_ReportOn_Produces_One_UnionAll()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale));
        CountOccurrences(sql, "UNION ALL").Should().Be(1);
    }

    [Fact]
    public void Purchase_ReportOn_Produces_One_UnionAll()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Purchase));
        CountOccurrences(sql, "UNION ALL").Should().Be(1);
    }

    // --------------------------------------------------------------------------
    // FACT 6: Date filter MUST be sargable (no CONVERT() on column).
    // Regression guard for _DOCS/CATALOGUE_PRODUCTION_AUDIT.md §3 (perf fix).
    // If anyone re-introduces CONVERT(DATE, h.DateTrans) BETWEEN ..., this fails.
    // --------------------------------------------------------------------------
    [Fact]
    public void DateFilter_IsSargable_NoConvertOnColumn()
    {
        var sql = InvokeBuildUnionAll(MakeFilter());

        // Look for non-sargable patterns specifically inside WHERE clauses.
        // The SELECT projection may legitimately use CONVERT(DATE, h.DateTrans) AS DateTrans
        // for output formatting — that does not break index seeks.
        sql.Should().NotMatchRegex(@"WHERE\s+CONVERT\(\s*DATE\s*,\s*h\.DateTrans\s*\)",
            because: "function on column kills index seek; use h.DateTrans >= @DateFrom AND h.DateTrans < DATEADD(DAY, 1, @DateTo)");
        sql.Should().NotMatchRegex(@"WHERE\s+CONVERT\(\s*DATETIME\s*,\s*h\.DateTrans\s*\)",
            because: "same — sargable form required");
        sql.Should().Contain(">= @DateFrom",
            because: "expected sargable form");
        sql.Should().Contain("DATEADD(DAY, 1, @DateTo)",
            because: "expected half-open upper bound when UseDateTime=false");
    }

    [Fact]
    public void DateFilter_UseDateTime_UsesInclusiveUpperBound()
    {
        var f = MakeFilter();
        f.UseDateTime = true;
        var sql = InvokeBuildUnionAll(f);

        sql.Should().Contain(">= @DateFrom");
        sql.Should().Contain("<= @DateTo",
            because: "with explicit time range, exact inclusive upper bound");
        sql.Should().NotContain("DATEADD(DAY, 1, @DateTo)",
            because: "DATETIME mode preserves the user-supplied time, no day-rollover");
    }

    [Fact]
    public void DateFilter_SessionDateBasis_FiltersOnSessionDateTime()
    {
        var f = MakeFilter();
        f.DateBasis = CatalogueDateBasis.SessionDate;
        var sql = InvokeBuildUnionAll(f);

        sql.Should().Contain("h.SessionDateTime >= @DateFrom");
        sql.Should().NotMatchRegex(@"WHERE\s+h\.DateTrans\s*>=\s*@DateFrom",
            because: "SessionDate basis must drive the filter, not h.DateTrans");
    }

    // --------------------------------------------------------------------------
    // FACT 7: New filter dimensions (PaymentType, ZReport, Town, User) are wired
    //         to the correct columns and applied to the correct legs.
    // Mirrors legacy repPowerReportCatalogue.aspx.vb:3760-3795.
    // --------------------------------------------------------------------------
    [Fact]
    public void PaymentTypeFilter_SaleLegOnly_AppliedToInvoiceAndCredit_NotPurchase()
    {
        var f = MakeFilter(CatalogueReportOn.Both);
        f.ItemsSelection = new ItemsSelectionFilter();
        f.ItemsSelection.PaymentTypes.Mode = FilterMode.Include;
        f.ItemsSelection.PaymentTypes.Ids = new List<string> { "CASH" };

        var sql = InvokeBuildUnionAll(f);
        var invoiceLeg = ExtractLeg(sql, "tbl_InvoiceDetails", upToTable: "tbl_CreditDetails");
        var purchLeg = ExtractLeg(sql, "tbl_PurchInvoiceDetails", upToTable: "tbl_PurchReturnDetails");

        invoiceLeg.Should().Contain("h.fk_PayTypeCode IN (",
            because: "PaymentType filter is sale-leg only and must apply to invoice header");
        purchLeg.Should().NotContain("h.fk_PayTypeCode",
            because: "purchase headers don't have fk_PayTypeCode; legacy never appends it");
    }

    [Fact]
    public void ZReportFilter_SaleLegOnly_AppliedToInvoiceAndCredit_NotPurchase()
    {
        var f = MakeFilter(CatalogueReportOn.Both);
        f.ItemsSelection = new ItemsSelectionFilter();
        f.ItemsSelection.ZReports.Mode = FilterMode.Include;
        f.ItemsSelection.ZReports.Ids = new List<string> { "Z001" };

        var sql = InvokeBuildUnionAll(f);
        var invoiceLeg = ExtractLeg(sql, "tbl_InvoiceDetails", upToTable: "tbl_CreditDetails");
        var purchLeg = ExtractLeg(sql, "tbl_PurchInvoiceDetails", upToTable: "tbl_PurchReturnDetails");

        invoiceLeg.Should().Contain("h.fk_ZReport IN (");
        purchLeg.Should().NotContain("h.fk_ZReport",
            because: "purchase headers don't have fk_ZReport");
    }

    [Fact]
    public void TownFilter_SaleLegOnly_AppliedToCustomerEntity_NotSupplier()
    {
        var f = MakeFilter(CatalogueReportOn.Both);
        f.ItemsSelection = new ItemsSelectionFilter();
        f.ItemsSelection.Towns.Mode = FilterMode.Include;
        f.ItemsSelection.Towns.Ids = new List<string> { "Belgrade" };

        var sql = InvokeBuildUnionAll(f);
        var invoiceLeg = ExtractLeg(sql, "tbl_InvoiceDetails", upToTable: "tbl_CreditDetails");
        var purchLeg = ExtractLeg(sql, "tbl_PurchInvoiceDetails", upToTable: "tbl_PurchReturnDetails");

        invoiceLeg.Should().Contain("e.Town IN (",
            because: "Town comes from customer entity (e) and only applies to sale legs");
        purchLeg.Should().NotContain("e.Town",
            because: "purchase entity is supplier — does not have a Town column to filter on");
    }

    [Fact]
    public void UserFilter_BothLegs_AppliedEverywhere()
    {
        var f = MakeFilter(CatalogueReportOn.Both);
        f.ItemsSelection = new ItemsSelectionFilter();
        f.ItemsSelection.Users.Mode = FilterMode.Include;
        f.ItemsSelection.Users.Ids = new List<string> { "ADMIN" };

        var sql = InvokeBuildUnionAll(f);

        // User filter must appear in EVERY leg (h.fk_UserCode exists on all 4 header tables).
        // We expect 4 occurrences of the IN clause — one per leg.
        var occurrences = CountOccurrences(sql, "h.fk_UserCode IN (");
        occurrences.Should().Be(4,
            because: "fk_UserCode exists on every header — filter must apply to all 4 UNION legs");
    }

    // --------------------------------------------------------------------------
    // FACT 8: New display columns (Price 4-10 Excl/Incl, InvPrice Excl/Incl)
    //         are projected by every UNION leg in detailed mode.
    // Mirrors legacy DisplayColumnE.Price4Excl..Price10Incl + InvPriceExcl/Incl
    // (repPowerReportCatalogue.aspx.vb:232-250).
    // --------------------------------------------------------------------------
    [Fact]
    public void Price4to10_AreProjected_InEveryLeg_DetailMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both));

        for (int n = 4; n <= 10; n++)
        {
            CountOccurrences(sql, $"AS Price{n}Excl").Should().BeGreaterOrEqualTo(4,
                because: $"Price{n}Excl must be projected in all 4 UNION legs (1 invoice + 1 credit + 1 purch + 1 purch return)");
            CountOccurrences(sql, $"AS Price{n}Incl").Should().BeGreaterOrEqualTo(4,
                because: $"Price{n}Incl must be projected in all 4 UNION legs");
        }
    }

    [Fact]
    public void InvPriceExclIncl_AreProjected_InEveryLeg_DetailMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both));

        // d.ItemPriceExcl/Incl exist on every detail table — projected in every leg.
        CountOccurrences(sql, "AS InvPriceExcl").Should().BeGreaterOrEqualTo(4,
            because: "InvPriceExcl must be projected in every UNION leg");
        CountOccurrences(sql, "AS InvPriceIncl").Should().BeGreaterOrEqualTo(4);
        sql.Should().Contain("d.ItemPriceExcl",
            because: "InvPriceExcl source column comes from each detail table");
        sql.Should().Contain("d.ItemPriceIncl");
    }

    [Fact]
    public void Price4to10_AreZeroPlaceholders_InSummaryMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale, CatalogueReportMode.Summary));

        // Summary mode collapses items to one row per item; per-row prices become placeholders.
        sql.Should().Contain("CAST(0 AS DECIMAL(18,4)) AS Price4Excl");
        sql.Should().Contain("CAST(0 AS DECIMAL(18,4)) AS Price10Incl");
        sql.Should().Contain("CAST(0 AS DECIMAL(18,4)) AS InvPriceExcl");
    }

    // --------------------------------------------------------------------------
    // FACT 8b: Header-derived display columns (Pass B) — Payment/Agent/ZReport/Station are
    //          sale-only (their FK columns do not exist on purchase headers); Franchise is
    //          store-derived and emitted on every leg via the 's' alias.
    // Mirrors legacy DimensionFilterBuilder.BuildSaleOnly availability rules.
    // --------------------------------------------------------------------------
    [Fact]
    public void PassB_SaleOnlyDisplayColumns_AreNotJoinedOnPurchaseLegs_DetailMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both, CatalogueReportMode.Detailed));

        var invoiceLeg  = ExtractLeg(sql, "tbl_InvoiceDetails",      upToTable: "tbl_CreditDetails");
        var creditLeg   = ExtractLeg(sql, "tbl_CreditDetails",       upToTable: "tbl_PurchInvoiceDetails");
        var purchLeg    = ExtractLeg(sql, "tbl_PurchInvoiceDetails", upToTable: "tbl_PurchReturnDetails");
        var purchRetLeg = ExtractLeg(sql, "tbl_PurchReturnDetails");

        // Sale legs join the lookups...
        invoiceLeg.Should().Contain("LEFT JOIN tbl_paymtype pt");
        invoiceLeg.Should().Contain("LEFT JOIN tbl_Agent ag");
        invoiceLeg.Should().Contain("LEFT JOIN tbl_Station st");
        creditLeg.Should().Contain("LEFT JOIN tbl_paymtype pt");

        // ...purchase legs do not (purchase headers have no fk_PayTypeCode/fk_AgentID/fk_StationCode).
        purchLeg.Should().NotContain("LEFT JOIN tbl_paymtype pt");
        purchLeg.Should().NotContain("LEFT JOIN tbl_Agent ag");
        purchLeg.Should().NotContain("LEFT JOIN tbl_Station st");
        purchRetLeg.Should().NotContain("LEFT JOIN tbl_paymtype pt");
    }

    [Fact]
    public void PassB_FranchiseJoin_AppliedOnEveryLeg_DetailMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both, CatalogueReportMode.Detailed));
        CountOccurrences(sql, "LEFT JOIN tbl_Franchise fr").Should().Be(4,
            because: "franchise lookup is store-derived and applies to all 4 UNION legs");
    }

    [Fact]
    public void PassB_DisplayColumns_AreProjected_InEveryLeg_DetailMode()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Both, CatalogueReportMode.Detailed));
        var needles = new[] { "AS PaymentType", "AS AgentName", "AS ZReportNumber",
                              "AS StationCode", "AS StationName", "AS FranchiseName" };
        foreach (var needle in needles)
        {
            CountOccurrences(sql, needle).Should().BeGreaterOrEqualTo(4,
                because: $"'{needle}' must be projected in every UNION leg so the outer SELECT can pass it through");
        }
    }

    [Fact]
    public void PassB_SummaryMode_Placeholders_NoHeaderJoins()
    {
        var sql = InvokeBuildUnionAll(MakeFilter(CatalogueReportOn.Sale, CatalogueReportMode.Summary));
        // Summary mode collapses everything to per-item; header lookups are not joined.
        sql.Should().NotContain("LEFT JOIN tbl_paymtype pt");
        sql.Should().NotContain("LEFT JOIN tbl_Agent ag");
        sql.Should().NotContain("LEFT JOIN tbl_Station st");
        sql.Should().Contain("'' AS PaymentType");
        sql.Should().Contain("'' AS FranchiseName");
    }

    // --------------------------------------------------------------------------
    // FACT 9: KPI totals must SUM stock without per-item dedup (legacy parity).
    // Powersoft365 grid totals do NOT dedup TotalStockQty across transaction
    // rows for the same item. KPI cards must match the grid 1:1.
    // Decision locked-in: Option A in CATALOGUE_PRODUCTION_AUDIT.md.
    // If you "fix" this to dedup, you break parity with Powersoft365.
    // --------------------------------------------------------------------------
    [Fact]
    public void TotalsQuery_StockTotals_AreFlatSum_NoDedup_LegacyParity()
    {
        var repo = new CatalogueRepository(DummyConnString);
        var t = typeof(CatalogueRepository);

        var buildItemFilters = t.GetMethod("BuildItemFilters", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildItemFilters not found");
        var buildTotals = t.GetMethod("BuildTotalsQuery", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildTotalsQuery not found");

        var filter = MakeFilter(CatalogueReportOn.Both);
        var itemFilters = buildItemFilters.Invoke(repo, new object[] { filter })!;
        var sql = (string)buildTotals.Invoke(repo, new[] { filter, itemFilters })!;

        sql.Should().NotContain("StockPerItem",
            because: "legacy Powersoft365 sums stock per transaction row without per-item dedup; " +
                     "introducing a StockPerItem CTE would diverge from Powersoft365 totals");
        sql.Should().NotContain("MaxSQ");
        sql.Should().NotContain("MaxSV");
        sql.Should().Contain("SUM(SQ)",
            because: "stock qty must be a flat SUM across the AllLegs CTE — matches legacy grid totals");
        sql.Should().Contain("SUM(SV)",
            because: "stock value must be a flat SUM across the AllLegs CTE — matches legacy grid totals");
    }

    [Fact]
    public void NoNewFilter_NoExtraWhereFragments()
    {
        // Sanity: when no PT/ZR/Town/User filter is set, no new clause must appear.
        var sql = InvokeBuildUnionAll(MakeFilter());
        sql.Should().NotContain("fk_PayTypeCode IN");
        sql.Should().NotContain("fk_ZReport IN");
        sql.Should().NotContain("e.Town IN");
        sql.Should().NotContain("h.fk_UserCode IN");
    }

    // --------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------
    private static string ExtractLeg(string fullSql, string detailTable, string? upToTable = null)
    {
        var startIdx = fullSql.IndexOf(detailTable, StringComparison.Ordinal);
        if (startIdx < 0) return string.Empty;

        // Walk backwards to find the SELECT that opens this leg
        var selectIdx = fullSql.LastIndexOf("SELECT", startIdx, StringComparison.Ordinal);
        if (selectIdx < 0) selectIdx = startIdx;

        int endIdx;
        if (upToTable != null)
        {
            endIdx = fullSql.IndexOf(upToTable, startIdx, StringComparison.Ordinal);
            if (endIdx < 0) endIdx = fullSql.Length;
        }
        else
        {
            // To end of leg = next UNION ALL after startIdx, or end of sql
            var nextUnion = fullSql.IndexOf("UNION ALL", startIdx, StringComparison.Ordinal);
            endIdx = nextUnion > 0 ? nextUnion : fullSql.Length;
        }

        return fullSql.Substring(selectIdx, endIdx - selectIdx);
    }

    private static int CountOccurrences(string source, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int count = 0;
        int idx = 0;
        while ((idx = source.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
