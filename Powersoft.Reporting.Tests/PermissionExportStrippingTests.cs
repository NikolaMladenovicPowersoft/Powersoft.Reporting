using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Powersoft.Reporting.Core.Enums;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Services;
using Xunit;

namespace Powersoft.Reporting.Tests;

/// <summary>
/// Verifies the ViewCost / ViewSupplier permission flags (legacy actions 6015 / 1200)
/// strip cost/profit/supplier columns from generated export files. These are the
/// authoritative server-side enforcement points used by interactive exports AND the
/// background scheduler — a regression here leaks cost to restricted users.
/// </summary>
public class PermissionExportStrippingTests
{
    private static string ToText(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

    // ---------------- Catalogue ----------------

    private static (List<CatalogueRow> rows, CatalogueFilter filter) BuildCatalogueSample()
    {
        var filter = new CatalogueFilter
        {
            ReportMode = CatalogueReportMode.Detailed,
            PrimaryGroup = CatalogueGroupBy.None,
            SecondaryGroup = CatalogueGroupBy.None,
            ThirdGroup = CatalogueGroupBy.None,
            DisplayColumns = new List<string>
            {
                "ItemCode", "ItemName", "Quantity", "NetValue",
                "Profit", "Markup", "Margin", "Cost", "TotalCost", "ItemSupplier"
            }
        };
        var rows = new List<CatalogueRow>
        {
            new CatalogueRow
            {
                ItemCode = "IT1", ItemDescription = "Item One",
                Quantity = 5, NetValue = 100m,
                ProfitValue = 40m,
                Cost = 12m, TotalCost = 60m, ItemSupplierName = "ACME Ltd"
            }
        };
        return (rows, filter);
    }

    [Fact]
    public void CatalogueCsv_NoCost_OmitsCostAndProfitColumns()
    {
        var (rows, filter) = BuildCatalogueSample();
        var csv = ToText(new CsvExportService().GenerateCatalogueCsv(rows, null, filter, viewCost: false, viewSupplier: true));

        csv.Should().NotContain("Profit");
        csv.Should().NotContain("Markup");
        csv.Should().NotContain("Margin");
        csv.Should().NotContain("Cost");     // covers both "Cost" and "Total Cost"
        csv.Should().Contain("ACME Ltd");    // supplier still allowed (viewSupplier: true)
    }

    [Fact]
    public void CatalogueCsv_NoSupplier_OmitsSupplierColumn()
    {
        var (rows, filter) = BuildCatalogueSample();
        var csv = ToText(new CsvExportService().GenerateCatalogueCsv(rows, null, filter, viewCost: true, viewSupplier: false));

        csv.Should().NotContain("Supplier");
        csv.Should().NotContain("ACME Ltd");
    }

    [Fact]
    public void CatalogueCsv_FullAccess_IncludesCostProfitSupplier()
    {
        var (rows, filter) = BuildCatalogueSample();
        var csv = ToText(new CsvExportService().GenerateCatalogueCsv(rows, null, filter, viewCost: true, viewSupplier: true));

        csv.Should().Contain("Profit");
        csv.Should().Contain("Cost");
        csv.Should().Contain("Supplier");
        csv.Should().Contain("ACME Ltd");
    }

    // ---------------- Offers ----------------

    private static (List<OffersReportRow> rows, OffersReportFilter filter) BuildOffersSample()
    {
        var filter = new OffersReportFilter { PrimaryGroup = "NONE", SecondaryGroup = "NONE" };
        var rows = new List<OffersReportRow>
        {
            new OffersReportRow
            {
                OfferNo = "OF1", StatusName = "Open",
                InvoiceGrandTotal = 200m, TotalItemCost = 77m, OrderPercentage = 50m
            }
        };
        return (rows, filter);
    }

    [Fact]
    public void OffersCsv_NoCost_OmitsCostColumn()
    {
        var (rows, filter) = BuildOffersSample();
        var csv = ToText(new CsvExportService().GenerateOffersReportCsv(rows, filter, viewCost: false));

        csv.Should().NotContain("Cost");
        csv.Should().NotContain("77");
    }

    [Fact]
    public void OffersCsv_FullAccess_IncludesCostColumn()
    {
        var (rows, filter) = BuildOffersSample();
        var csv = ToText(new CsvExportService().GenerateOffersReportCsv(rows, filter, viewCost: true));

        csv.Should().Contain("Cost");
        csv.Should().Contain("77");
    }

    // ---------------- Pareto ----------------

    private static (ParetoResult result, ParetoFilter filter) BuildParetoSample()
    {
        var filter = new ParetoFilter { Dimension = ParetoDimension.Item, Metric = ParetoMetric.Value };
        var result = new ParetoResult
        {
            Rows = new List<ParetoRow>
            {
                new ParetoRow { Rank = 1, Code = "IT1", Name = "Item One", Quantity = 5, Subtotal = 100m, Profit = 33m, Classification = "A" }
            }
        };
        return (result, filter);
    }

    [Fact]
    public void ParetoCsv_NoCost_OmitsProfitColumn()
    {
        var (result, filter) = BuildParetoSample();
        var csv = ToText(new CsvExportService().GenerateParetoCsv(result, filter, viewCost: false));

        csv.Should().NotContain("Profit");
        csv.Should().NotContain("33");
    }

    [Fact]
    public void ParetoCsv_FullAccess_IncludesProfitColumn()
    {
        var (result, filter) = BuildParetoSample();
        var csv = ToText(new CsvExportService().GenerateParetoCsv(result, filter, viewCost: true));

        csv.Should().Contain("Profit");
        csv.Should().Contain("33");
    }
}
