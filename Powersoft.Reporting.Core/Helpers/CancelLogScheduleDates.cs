namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Normalizes date bounds for scheduled Cancel Log runs to match legacy repCancelLog.aspx.vb
/// (DateFrom 00:00:00, DateTo 23:59:59 when using date+time mode).
/// </summary>
public static class CancelLogScheduleDates
{
    public static (DateTime DateFrom, DateTime DateTo) Normalize(DateTime dateFrom, DateTime dateTo, bool reportByDateTime)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;

        if (reportByDateTime)
            to = to.AddHours(23).AddMinutes(59).AddSeconds(59);

        return (from, to);
    }
}
