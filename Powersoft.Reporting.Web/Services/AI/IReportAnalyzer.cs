using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services.AI;

public interface IReportAnalyzer
{
    bool IsConfigured { get; }
    Task<ReportAnalysis> AnalyzeAsync(string csvData, string reportType, string? locale = null, string? customSystemPrompt = null, CancellationToken ct = default);

    /// <summary>
    /// Multi-turn chat: sends an array of previous messages plus a new user message.
    /// Returns the assistant's reply as plain text.
    /// </summary>
    Task<AiChatReply> ChatAsync(List<AiChatMessage> history, string newUserMessage, CancellationToken ct = default);
}

public record AiChatMessage(string Role, string Content);

public record AiChatReply(string Content, int InputTokens, int OutputTokens, double DurationMs);
