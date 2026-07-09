using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Builds the presentation Cash Flow statement (Group → Category → optional accounts) from the
/// repository result. Single source of truth shared by the screen JSON, print preview, CSV/Excel/PDF
/// exports and the scheduler, so every output shows identical numbers.
///
/// Every mapping category is always rendered (empty ones as zero lines, like the PBI matrix);
/// unmapped accounts surface in a trailing "(Unassigned)" group instead of being dropped.
/// </summary>
public static class CashFlowStatementBuilder
{
    public const string UnassignedName = "(Unassigned)";
    public const string BankGroupName = "Bank";
    private const int UnassignedSort = 999999;

    public static CashFlowStatement Build(CashFlowResult result, CashFlowFilter filter)
    {
        var rows = result.Rows;

        // Net cash movement must reconcile with the DISPLAYED statement, so it is derived from the
        // mapping's Bank group (all groups sum to 0, hence net = -(Bank group)). Fall back to the
        // tbl_accbank flag only when the mapping has no Bank group at all.
        Func<CashFlowRow, bool> isBankLine =
            rows.Any(r => IsBankGroup(r.GroupName))
                ? r => IsBankGroup(r.GroupName)
                : r => r.IsBank;

        var statement = new CashFlowStatement
        {
            Months = result.Months,
            AccountRowCount = rows.Count,
            NetCashMovement = -rows.Where(isBankLine).Sum(r => r.Amount),
            PriorNetCashMovement = -rows.Where(isBankLine).Sum(r => r.PriorAmount),
            TotalIn = rows.Where(r => !isBankLine(r) && r.Amount > 0).Sum(r => r.Amount),
            TotalOut = rows.Where(r => !isBankLine(r) && r.Amount < 0).Sum(r => r.Amount)
        };

        foreach (var month in result.Months)
        {
            statement.MonthNetCashMovement[month] =
                -rows.Where(isBankLine).Sum(r => r.MonthAmounts?.GetValueOrDefault(month) ?? 0m);
        }

        var dataByCategory = rows
            .GroupBy(r => (
                Group: string.IsNullOrEmpty(r.GroupName) ? UnassignedName : r.GroupName,
                GroupSort: string.IsNullOrEmpty(r.GroupName) ? UnassignedSort : r.GroupSortOrder,
                Category: string.IsNullOrEmpty(r.GroupName) ? UnassignedName : r.CategoryName,
                CategorySort: string.IsNullOrEmpty(r.GroupName) ? UnassignedSort : r.CategorySortOrder))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Skeleton (all configured categories) + any data category missing from the skeleton.
        var lineKeys = result.Categories
            .Select(c => (Group: c.GroupName, GroupSort: c.GroupSortOrder,
                          Category: c.CategoryName, CategorySort: c.CategorySortOrder))
            .ToList();
        foreach (var key in dataByCategory.Keys)
        {
            if (!lineKeys.Contains(key))
                lineKeys.Add(key);
        }

        foreach (var groupSet in lineKeys
                     .OrderBy(k => k.GroupSort).ThenBy(k => k.CategorySort)
                     .GroupBy(k => (k.Group, k.GroupSort)))
        {
            var group = new CashFlowStatementGroup
            {
                Name = groupSet.Key.Group,
                SortOrder = groupSet.Key.GroupSort,
                MonthAmounts = result.Months.Count > 0 ? new Dictionary<string, decimal>() : null
            };

            foreach (var key in groupSet)
            {
                var accounts = dataByCategory.TryGetValue(key, out var list) ? list : new List<CashFlowRow>();

                var cat = new CashFlowStatementCategory
                {
                    Name = key.Category,
                    SortOrder = key.CategorySort,
                    IsEmpty = accounts.Count == 0,
                    Amount = accounts.Sum(a => a.Amount),
                    PriorAmount = accounts.Sum(a => a.PriorAmount),
                    BudgetAmount = accounts.Sum(a => a.BudgetAmount),
                    Accounts = filter.ShowAccounts
                        ? accounts.OrderBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(a => a.AccountCode, StringComparer.Ordinal).ToList()
                        : new List<CashFlowRow>()
                };

                if (result.Months.Count > 0)
                {
                    cat.MonthAmounts = new Dictionary<string, decimal>();
                    foreach (var month in result.Months)
                    {
                        var v = accounts.Sum(a => a.MonthAmounts?.GetValueOrDefault(month) ?? 0m);
                        cat.MonthAmounts[month] = v;
                        group.MonthAmounts![month] = group.MonthAmounts.GetValueOrDefault(month) + v;
                    }
                }

                group.Amount += cat.Amount;
                group.PriorAmount += cat.PriorAmount;
                group.BudgetAmount += cat.BudgetAmount;
                group.Categories.Add(cat);
            }

            statement.Groups.Add(group);
        }

        return statement;
    }

    private static bool IsBankGroup(string? groupName) =>
        string.Equals(groupName, BankGroupName, StringComparison.OrdinalIgnoreCase);
}
