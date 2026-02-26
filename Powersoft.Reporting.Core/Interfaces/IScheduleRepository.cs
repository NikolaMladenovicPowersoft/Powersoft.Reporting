using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IScheduleRepository
{
    Task<int> CreateScheduleAsync(ReportSchedule schedule);
    Task<List<ReportSchedule>> GetSchedulesForReportAsync(string reportType);
    Task<ReportSchedule?> GetScheduleByIdAsync(int scheduleId);
    Task<bool> UpdateScheduleAsync(ReportSchedule schedule);
    Task<bool> DeleteScheduleAsync(int scheduleId);
    Task<int> CountActiveSchedulesForReportAsync(string reportType);

    /// <summary>
    /// Returns all active schedules whose NextRunDate has passed (due for execution).
    /// </summary>
    Task<List<ReportSchedule>> GetDueSchedulesAsync(DateTime asOfUtc);

    /// <summary>
    /// Updates LastRunDate and computes the next NextRunDate after execution.
    /// For "Once" schedules, sets IsActive = 0 after execution.
    /// </summary>
    Task UpdateAfterExecutionAsync(int scheduleId, DateTime lastRunDate, DateTime? nextRunDate, bool deactivate);

    /// <summary>
    /// Inserts an execution log entry into tbl_ReportScheduleLog.
    /// </summary>
    Task<int> InsertScheduleLogAsync(ScheduleLog log);
}
