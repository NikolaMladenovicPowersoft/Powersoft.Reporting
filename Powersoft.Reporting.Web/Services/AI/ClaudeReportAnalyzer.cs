using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services.AI;

public class ClaudeReportAnalyzer : IReportAnalyzer
{
    private readonly AiAnalyzerOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<ClaudeReportAnalyzer> _logger;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public ClaudeReportAnalyzer(
        IOptions<AiAnalyzerOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ClaudeReportAnalyzer> logger)
    {
        _options = options.Value;
        _http = httpClientFactory.CreateClient("ClaudeAI");
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<ReportAnalysis> AnalyzeAsync(
        string csvData, string reportType, string? locale = null, string? customSystemPrompt = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI Analyzer is not configured. Please set the API key in Settings.");

        var sw = Stopwatch.StartNew();
        var systemPrompt = BuildSystemPrompt(reportType, locale, customSystemPrompt);
        var userPrompt = BuildUserPrompt(csvData, reportType);

        var requestBody = new
        {
            model = _options.Model,
            max_tokens = _options.MaxOutputTokens,
            temperature = _options.Temperature,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        _logger.LogInformation("Sending {ReportType} analysis request to Claude ({Model})", reportType, _options.Model);

        var response = await _http.SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error {Status}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {ExtractErrorMessage(responseJson)}");
        }

        sw.Stop();
        return ParseResponse(responseJson, sw.ElapsedMilliseconds);
    }

    public async Task<AiChatReply> ChatAsync(List<AiChatMessage> history, string newUserMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI Analyzer is not configured.");

        var sw = Stopwatch.StartNew();

        string? systemContent = null;
        var messages = new List<object>();
        foreach (var msg in history)
        {
            if (msg.Role == "system") { systemContent = msg.Content; continue; }
            messages.Add(new { role = msg.Role, content = msg.Content });
        }
        messages.Add(new { role = "user", content = newUserMessage });

        var requestBody = new
        {
            model = _options.Model,
            max_tokens = _options.MaxOutputTokens,
            temperature = _options.Temperature,
            system = systemContent ?? "",
            messages
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        _logger.LogInformation("Sending chat follow-up to Claude ({Model})", _options.Model);

        var response = await _http.SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude chat error {Status}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {ExtractErrorMessage(responseJson)}");
        }

        sw.Stop();

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var content = "";
        if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && block.TryGetProperty("text", out var txt))
                { content = txt.GetString() ?? ""; break; }
            }
        }

        int inTok = 0, outTok = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it)) inTok = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) outTok = ot.GetInt32();
        }

        _logger.LogInformation("Claude chat completed in {Ms}ms — {In}+{Out} tokens", sw.ElapsedMilliseconds, inTok, outTok);
        return new AiChatReply(content, inTok, outTok, sw.ElapsedMilliseconds);
    }

    private ReportAnalysis ParseResponse(string responseJson, double durationMs)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var textContent = "";
        if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && block.TryGetProperty("text", out var txt))
                {
                    textContent = txt.GetString() ?? "";
                    break;
                }
            }
        }

        var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it)) inputTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) outputTokens = ot.GetInt32();
        }

        var analysis = ParseStructuredAnalysis(textContent);
        analysis.ModelUsed = model;
        analysis.InputTokens = inputTokens;
        analysis.OutputTokens = outputTokens;
        analysis.DurationMs = durationMs;

        _logger.LogInformation(
            "Claude analysis completed in {Ms}ms — {InTok} input / {OutTok} output tokens",
            durationMs, inputTokens, outputTokens);

        return analysis;
    }

    private static ReportAnalysis ParseStructuredAnalysis(string text)
    {
        var analysis = new ReportAnalysis();

        try
        {
            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = text[jsonStart..(jsonEnd + 1)];
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                if (root.TryGetProperty("summary", out var s))
                    analysis.Summary = s.GetString() ?? "";
                if (root.TryGetProperty("keyFindings", out var kf) && kf.ValueKind == JsonValueKind.Array)
                    analysis.KeyFindings = kf.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();
                if (root.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array)
                    analysis.Alerts = a.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();
                if (root.TryGetProperty("recommendations", out var r) && r.ValueKind == JsonValueKind.Array)
                    analysis.Recommendations = r.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();

                return analysis;
            }
        }
        catch { /* fall through to plain text parsing */ }

        analysis.Summary = text;
        return analysis;
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Unknown error";
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }

    private static string BuildSystemPrompt(string reportType, string? locale, string? customSystemPrompt = null)
    {
        var lang = (locale ?? "el").ToLowerInvariant() switch
        {
            "el" => "Respond in Greek (Ελληνικά).",
            "en" => "Respond in English.",
            "sr" => "Respond in Serbian (latin script).",
            "de" => "Respond in German.",
            "fr" => "Respond in French.",
            "bg" => "Respond in Bulgarian.",
            "ru" => "Respond in Russian (Русский).",
            "uk" => "Respond in Ukrainian (Українська).",
            "et" => "Respond in Estonian (Eesti).",
            "nl" => "Respond in Dutch (Nederlands).",
            _ => "Respond in English."
        };

        var customSection = !string.IsNullOrWhiteSpace(customSystemPrompt)
            ? $"\n\nADDITIONAL INSTRUCTIONS FROM USER:\n{customSystemPrompt}\n"
            : "";

        return $@"You are a senior financial data analyst working for a retail/ERP company. 
You analyze sales, purchasing, and inventory reports with precision.
{customSection}
Your task: analyze the CSV report data provided and return a JSON object with exactly this structure:
{{
  ""summary"": ""A concise 2-3 sentence overview of the report's key story"",
  ""keyFindings"": [""Finding 1"", ""Finding 2"", ""Finding 3""],
  ""alerts"": [""Any concerning trends or anomalies worth flagging""],
  ""recommendations"": [""Actionable business recommendations based on the data""]
}}

Rules:
- Focus on business-relevant insights, not just restating numbers.
- Identify trends, anomalies, outliers, and comparisons where data allows.
- For sales reports: look at basket size, transaction count, revenue per store/category.
- For purchase vs sales: look at stock coverage, sell-through %, margin analysis, overstock risk.
- Keep each finding and recommendation concise (1-2 sentences).
- Return ONLY the JSON object, no markdown fences or extra text.
- {lang}";
    }

    private static string BuildUserPrompt(string csvData, string reportType)
    {
        var reportDescription = reportType switch
        {
            "AverageBasket" => "Average Basket Report (sales performance: revenue, transactions, average basket value, items per transaction, grouped by period/store/category)",
            "PurchasesSales" => "Purchases vs Sales Report (purchasing vs selling quantities and values, profit margins, stock levels, sell-through percentage)",
            _ => $"Report: {reportType}"
        };

        return $@"Analyze this {reportDescription}.

DATA (CSV):
{csvData}";
    }
}
