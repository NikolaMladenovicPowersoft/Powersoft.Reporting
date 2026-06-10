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

    /// <summary>
    /// Returns recent schedule execution logs, optionally filtered by schedule ID.
    /// </summary>
    Task<List<ScheduleLogEntry>> GetScheduleLogsAsync(int? scheduleId = null, int top = 100);

    /// <summary>
    /// Updates the token usage on an existing log entry (called after AI analysis completes).
    /// </summary>
    Task UpdateLogTokensAsync(int logId, int inputTokens, int outputTokens, decimal estimatedCost);

    // AI Token budget
    Task<AiTokenBudget?> GetCurrentTokenBudgetAsync();
    Task<AiTokenBudget> GetOrCreateTokenBudgetAsync();
    Task<bool> IncrementTokenUsageAsync(int inputTokens, int outputTokens);
    Task<bool> SetMonthlyTokenLimitAsync(int limit);
    Task<bool> SetCostLimitsAsync(decimal softLimit, decimal hardLimit);
    Task<bool> ResetMonthlyUsageAsync();

    // Email templates
    Task<List<EmailTemplate>> GetEmailTemplatesAsync(string? reportType = null);
    Task<EmailTemplate?> GetEmailTemplateByIdAsync(int templateId);
    Task<EmailTemplate?> GetDefaultEmailTemplateAsync(string? reportType = null);
    Task<int> CreateEmailTemplateAsync(EmailTemplate template);
    Task<bool> UpdateEmailTemplateAsync(EmailTemplate template);
    Task<bool> DeleteEmailTemplateAsync(int templateId);

    // AI Prompt templates
    Task<List<AiPromptTemplate>> GetAiPromptTemplatesAsync(string? reportType = null);
    Task<AiPromptTemplate?> GetAiPromptTemplateByIdAsync(int templateId);
    Task<AiPromptTemplate?> GetDefaultAiPromptTemplateAsync(string? reportType = null);
    Task<int> CreateAiPromptTemplateAsync(AiPromptTemplate template);
    Task<bool> UpdateAiPromptTemplateAsync(AiPromptTemplate template);
    Task<bool> DeleteAiPromptTemplateAsync(int templateId);

    // All Schedules (cross-report)
    Task<List<(int Id, string ReportType, string Name, string CreatedBy, DateTime CreatedDate,
        bool IsActive, string RecurrenceType, DateTime? NextRun, DateTime? LastRun,
        string ExportFormat, string Recipients, byte? StarRating)>> GetAllSchedulesAsync();
    Task UpdateStarRatingAsync(int scheduleId, byte? rating);
}
