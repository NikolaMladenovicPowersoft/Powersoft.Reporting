using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IScheduleRepository
{
    Task<int> CreateScheduleAsync(ReportSchedule schedule);
    Task<List<ReportSchedule>> GetSchedulesForReportAsync(string reportType);
    Task<ReportSchedule?> GetScheduleByIdAsync(int scheduleId);
    Task<bool> UpdateScheduleAsync(ReportSchedule schedule);
    Task<bool> DeleteScheduleAsync(int scheduleId);
}
