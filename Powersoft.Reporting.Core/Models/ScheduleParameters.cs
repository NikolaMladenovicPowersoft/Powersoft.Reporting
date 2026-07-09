using Powersoft.Reporting.Core.Enums;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Deserialized from tbl_ReportSchedule.ParametersJson.
/// Contains everything needed to reproduce a report without a user session.
/// </summary>
public class ScheduleParameters
{
    // Average Basket specific
    public BreakdownType Breakdown { get; set; } = BreakdownType.Monthly;
    public GroupByType GroupBy { get; set; } = GroupByType.None;
    public GroupByType SecondaryGroupBy { get; set; } = GroupByType.None;
    public bool CompareLastYear { get; set; }

    // Purchases vs Sales specific
    public PsReportMode ReportMode { get; set; } = PsReportMode.Detailed;
    public PsGroupBy PrimaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy SecondaryGroup { get; set; } = PsGroupBy.None;
    public PsGroupBy ThirdGroup { get; set; } = PsGroupBy.None;
    public bool ShowProfit { get; set; } = true;
    public bool ShowStock { get; set; }
    public bool ShowOnOrder { get; set; }
    public bool ShowReservation { get; set; }
    public bool ShowAvailable { get; set; }
    public bool IncludeAdditionalCharges { get; set; } = true;
    public string? ReportType { get; set; }

    // Pareto 80/20 specific
    public string? ParetoDimension { get; set; }
    public string? ParetoMetric { get; set; }
    public int ProfitBasis { get; set; }
    public bool ExcludeNegativeAmounts { get; set; } = true;
    public decimal ClassAThreshold { get; set; } = 80;
    public decimal ClassBThreshold { get; set; } = 95;
    public decimal PriceInterval { get; set; } = 10;
    public int PriceOnIndex { get; set; }
    public bool PriceOnIncludesVat { get; set; }
    public int TimezoneOffsetMinutes { get; set; }

    // Charts specific (keys from collectChartParams() in Charts.cshtml)
    public string? ChartMode { get; set; }
    public string? ChartDimension { get; set; }
    public string? ChartMetric { get; set; }
    public int TopN { get; set; } = 10;
    public string? ChartType { get; set; }
    public bool ShowOthers { get; set; } = true;

    // CancelLog specific (distinct keys to avoid clashing with PS primaryGroup/reportType)
    public string? CancelActionType { get; set; }
    public string? CancelLogReportType { get; set; }
    public string? CancelLogPrimaryGroup { get; set; }
    public string? CancelLogSecondaryGroup { get; set; }
    public bool ReportByDateTime { get; set; }
    public int MaxRecords { get; set; } = 50000;

    // Catalogue specific (distinct keys to avoid clashing with PS reportMode/primaryGroup).
    // Note: showProfit/showStock/storeCodes/itemsSelection/sortColumn are shared and parsed above.
    public string? CatReportMode { get; set; }
    public string? CatReportOn { get; set; }
    public string? CatPrimaryGroup { get; set; }
    public string? CatSecondaryGroup { get; set; }
    public string? CatThirdGroup { get; set; }
    public string? CatDisplayColumns { get; set; }
    public string? CatDateBasis { get; set; }
    public bool CatUseDateTime { get; set; }
    public int CatProfitBasedOn { get; set; } = 99;
    public bool CatProfitIncludesVat { get; set; }
    public int CatStockValueBasedOn { get; set; } = 99;
    public bool CatStockValueIncludesVat { get; set; }
    public int CatCostType { get; set; } = 99;
    public string? CatColumnFilters { get; set; }

    // ProspectClients specific
    public string? PcDateField { get; set; }
    public string? PcStatusFilter { get; set; }
    public string? PcPriorityFilter { get; set; }
    public string? PcFollowedByFilter { get; set; }
    public string? PcCategory1Filter { get; set; }
    public string? PcCategory2Filter { get; set; }
    public string? PcPrimaryGroup { get; set; }
    public string? PcSecondaryGroup { get; set; }
    public bool PcIncludeHistory { get; set; }
    public string? PcCustomerCodesJson { get; set; }
    public bool PcCustomerExcludeMode { get; set; }

    // OffersReport specific
    public string? OrDateField { get; set; }
    public string? OrStatusFilter { get; set; }
    public string? OrStoreFilter { get; set; }
    public string? OrAgentFilter { get; set; }
    public string? OrPrimaryGroup { get; set; }
    public string? OrSecondaryGroup { get; set; }
    public string? OrThirdGroup { get; set; }
    public string? OrOfferType { get; set; }
    public bool OrIncludeHistory { get; set; }
    public string? OrCustomerCodesJson { get; set; }
    public bool OrCustomerExcludeMode { get; set; }
    public string? OrStatusCodesJson { get; set; }
    public string? OrStoreCodesJson { get; set; }
    public string? OrAgentCodesJson { get; set; }

    // Trial Balance specific (distinct Tb* keys to avoid clashing with shared params)
    public string? TbReportMode { get; set; }
    public bool TbIncludeZeroMovements { get; set; }
    public string? TbSelectedAccounts { get; set; }
    public string? TbSelectedHeaders { get; set; }
    public string? TbSuppressedHeaders { get; set; }

    // Items Not Purchased specific (distinct Cnp* keys). ReferenceDate is resolved to "today"
    // at execution time (a scheduled staleness report is always relative to the run date).
    public int CnpDaysThreshold { get; set; } = 30;
    public string? CnpGroupBy { get; set; }            // "Item" or "Customer"
    public bool CnpIncludeNeverPurchased { get; set; }
    public string? CnpCustomerCodesJson { get; set; }
    public bool CnpCustomerExcludeMode { get; set; }

    // Profit & Loss specific (distinct Pl* keys)
    public bool PlHeaderLevel { get; set; }
    public bool PlCompareToLastYear { get; set; }
    public decimal PlOpeningStockValue { get; set; }
    public decimal PlClosingStockValue { get; set; }
    public string? PlSuppressedHeaders { get; set; }

    // Cash Flow specific (distinct Cf* keys)
    public bool CfShowAccounts { get; set; }
    public bool CfCompareToLastYear { get; set; }
    public bool CfIncludeBudget { get; set; }
    public bool CfMonthly { get; set; }

    // Shared
    public bool IncludeVat { get; set; }
    public List<string>? StoreCodes { get; set; }
    public List<int>? ItemIds { get; set; }

    /// <summary>
    /// Full dimension filter JSON from _ItemsSelection.cshtml.
    /// Parsed at execution time via <see cref="Helpers.ItemsSelectionParser"/>.
    /// </summary>
    public string? ItemsSelectionJson { get; set; }

    /// <summary>
    /// Relative date range resolved at execution time.
    /// If null, defaults to LastNDays with Value=30.
    /// </summary>
    public ReportDateRangeOption? DateRange { get; set; }

    public string SortColumn { get; set; } = "Period";
    public string SortDirection { get; set; } = "ASC";

    // Permission snapshot — captured at schedule creation time.
    // The background worker has no user session, so permissions are baked in.
    // Default true = safe (shows data); false = strips cost/supplier columns from exports.
    public bool ViewCost { get; set; } = true;
    public bool ViewSupplier { get; set; } = true;
}
