using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services.AI;

public class OpenAIReportAnalyzer : IReportAnalyzer
{
    private readonly AiAnalyzerOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<OpenAIReportAnalyzer> _logger;

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAIReportAnalyzer(
        IOptions<AiAnalyzerOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAIReportAnalyzer> logger)
    {
        _options = options.Value;
        _http = httpClientFactory.CreateClient("OpenAI");
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<ReportAnalysis> AnalyzeAsync(
        string csvData, string reportType, string? locale = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI Analyzer is not configured. Please set the API key in Settings.");

        var sw = Stopwatch.StartNew();
        var systemPrompt = BuildSystemPrompt(reportType, locale);
        var userPrompt = BuildUserPrompt(csvData, reportType);

        var requestBody = new
        {
            model = _options.Model,
            max_tokens = _options.MaxOutputTokens,
            temperature = _options.Temperature,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        _logger.LogInformation("Sending {ReportType} analysis request to OpenAI ({Model})", reportType, _options.Model);

        var response = await _http.SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI API error {Status}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"OpenAI API returned {(int)response.StatusCode}: {ExtractErrorMessage(responseJson)}");
        }

        sw.Stop();
        return ParseResponse(responseJson, sw.ElapsedMilliseconds);
    }

    private ReportAnalysis ParseResponse(string responseJson, double durationMs)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var textContent = "";
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                textContent = content.GetString() ?? "";
        }

        var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
        }

        var analysis = ParseStructuredJson(textContent);
        analysis.ModelUsed = model;
        analysis.InputTokens = inputTokens;
        analysis.OutputTokens = outputTokens;
        analysis.DurationMs = durationMs;

        _logger.LogInformation(
            "OpenAI analysis completed in {Ms}ms — {InTok} input / {OutTok} output tokens",
            durationMs, inputTokens, outputTokens);

        return analysis;
    }

    private static ReportAnalysis ParseStructuredJson(string text)
    {
        var analysis = new ReportAnalysis();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("summary", out var s))
                analysis.Summary = s.GetString() ?? "";
            if (root.TryGetProperty("keyFindings", out var kf) && kf.ValueKind == JsonValueKind.Array)
                analysis.KeyFindings = kf.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();
            if (root.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array)
                analysis.Alerts = a.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();
            if (root.TryGetProperty("recommendations", out var r) && r.ValueKind == JsonValueKind.Array)
                analysis.Recommendations = r.EnumerateArray().Select(e => e.GetString() ?? "").Where(v => v != "").ToList();
        }
        catch
        {
            analysis.Summary = text;
        }
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

    private static string BuildSystemPrompt(string reportType, string? locale)
    {
        var lang = (locale ?? "el").ToLowerInvariant() switch
        {
            "el" => "Respond in Greek (Ελληνικά).",
            "en" => "Respond in English.",
            "sr" => "Respond in Serbian (latin script).",
            "de" => "Respond in German.",
            "fr" => "Respond in French.",
            "bg" => "Respond in Bulgarian.",
            _ => "Respond in English."
        };

        return $@"You are a senior financial data analyst working for a retail/ERP company.
You analyze sales, purchasing, and inventory reports with precision.

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
- {lang}";
    }

    private static string BuildUserPrompt(string csvData, string reportType)
    {
        var reportDescription = reportType switch
        {
            "AverageBasket" => "Average Basket Report (sales performance: revenue, transactions, average basket value, items per transaction)",
            "PurchasesSales" => "Purchases vs Sales Report (purchasing vs selling quantities and values, profit margins, stock levels, sell-through percentage)",
            _ => $"Report: {reportType}"
        };

        return $@"Analyze this {reportDescription}.

DATA (CSV):
{csvData}";
    }
}
