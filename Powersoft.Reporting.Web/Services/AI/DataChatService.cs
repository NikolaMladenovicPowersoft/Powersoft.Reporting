using System.Diagnostics;
using System.Text.Json;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Data.Tenant;

namespace Powersoft.Reporting.Web.Services.AI;

/// <summary>
/// Orchestrates the data chat pipeline:
/// 1. Build system prompt with curated DB schema
/// 2. Ask AI to generate a SQL query from the user's question
/// 3. Validate and execute the SQL safely
/// 4. Ask AI to interpret the results as a natural language answer
/// </summary>
public class DataChatService
{
    private readonly ReportAnalyzerFactory _analyzerFactory;
    private readonly ILogger<DataChatService> _logger;
    private const int MaxHistoryTurns = 20;

    public DataChatService(ReportAnalyzerFactory analyzerFactory, ILogger<DataChatService> logger)
    {
        _analyzerFactory = analyzerFactory;
        _logger = logger;
    }

    public bool IsConfigured => _analyzerFactory.IsConfigured;

    public async Task<DataChatResponse> AskAsync(
        string connectionString, DataChatRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int totalInput = 0, totalOutput = 0;

        try
        {
            var analyzer = _analyzerFactory.Create();
            var schema = SchemaProvider.GetCompactSchema();
            var locale = request.Locale ?? "en";

            // --- STEP 1: Generate SQL ---
            var sqlHistory = BuildSqlGenerationMessages(schema, request, locale);
            var sqlReply = await analyzer.ChatAsync(sqlHistory, request.Message, ct);
            totalInput += sqlReply.InputTokens;
            totalOutput += sqlReply.OutputTokens;

            var generatedSql = ExtractSql(sqlReply.Content);

            if (string.IsNullOrWhiteSpace(generatedSql))
            {
                // AI decided no SQL is needed — it's a conversational answer
                return new DataChatResponse
                {
                    Success = true,
                    Answer = CleanAnswer(sqlReply.Content),
                    InputTokens = totalInput,
                    OutputTokens = totalOutput,
                    DurationMs = sw.Elapsed.TotalMilliseconds
                };
            }

            _logger.LogInformation("DataChat SQL generated: {Sql}", generatedSql);

            // --- STEP 2: Validate and Execute ---
            var queryResult = await SafeQueryExecutor.ExecuteAsync(connectionString, generatedSql, ct);

            if (!queryResult.Success)
            {
                // SQL failed — ask AI to explain the error and try to answer anyway
                return new DataChatResponse
                {
                    Success = false,
                    Answer = $"I tried to query the database but encountered an error: {queryResult.ErrorMessage}",
                    ErrorMessage = queryResult.ErrorMessage,
                    InputTokens = totalInput,
                    OutputTokens = totalOutput,
                    DurationMs = sw.Elapsed.TotalMilliseconds
                };
            }

            // --- STEP 3: Interpret results ---
            var resultText = queryResult.ToTextTable(50);
            var interpretPrompt = BuildInterpretationPrompt(request.Message, generatedSql, resultText, queryResult, locale);
            var interpretHistory = new List<AiChatMessage>
            {
                new("system", interpretPrompt)
            };
            var interpretReply = await analyzer.ChatAsync(interpretHistory,
                $"The user asked: \"{request.Message}\"\nQuery returned {queryResult.RowCount} row(s). Please provide a clear answer.", ct);
            totalInput += interpretReply.InputTokens;
            totalOutput += interpretReply.OutputTokens;

            return new DataChatResponse
            {
                Success = true,
                Answer = CleanAnswer(interpretReply.Content),
                Columns = queryResult.Columns,
                Rows = queryResult.Rows.Count <= 100 ? queryResult.Rows : queryResult.Rows.Take(100).ToList(),
                RowCount = queryResult.RowCount,
                Truncated = queryResult.Truncated,
                InputTokens = totalInput,
                OutputTokens = totalOutput,
                DurationMs = sw.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataChat error for question: {Question}", request.Message);
            return new DataChatResponse
            {
                Success = false,
                Answer = "Sorry, I encountered an error processing your question. Please try rephrasing it.",
                ErrorMessage = ex.Message,
                DurationMs = sw.Elapsed.TotalMilliseconds
            };
        }
    }

    private static List<AiChatMessage> BuildSqlGenerationMessages(
        string schema, DataChatRequest request, string locale)
    {
        var systemPrompt = $@"You are a SQL Server expert assistant for a retail/accounting application.
The user will ask business questions about their data. Your job is to generate a T-SQL SELECT query to answer the question.

DATABASE SCHEMA:
{schema}

RULES:
1. Generate ONLY SELECT queries. Never INSERT, UPDATE, DELETE, or DDL.
2. Wrap your SQL in ```sql ... ``` code blocks.
3. If the question cannot be answered with the available schema, explain why — do NOT generate SQL.
4. Use TOP 500 to limit large result sets.
5. Always use ISNULL() for nullable columns in calculations.
6. For name display: if Company=1 use LastCompanyName, else FirstName + ' ' + LastCompanyName.
7. Net amount = Amount - (Discount + ExtraDiscount). Gross = Net + VatAmount.
8. When the user says ""sales"" they mean tbl_InvoiceHeader/Details (positive) minus tbl_CreditHeader/Details (returns).
9. When grouping by time: use CONVERT(DATE, DateTrans) for daily, DATEPART(MONTH, DateTrans) for monthly, DATEPART(YEAR, DateTrans) for yearly.
10. Active items: WHERE ISNULL(ItemActive, 1) = 1
11. Respond in {locale} language for explanations, but SQL must always be valid T-SQL.
12. If the question is conversational (greeting, thank you, etc.) just reply normally without SQL.";

        var messages = new List<AiChatMessage> { new("system", systemPrompt) };

        // Add conversation history (limited)
        var historyToSend = request.History
            .Where(h => h.Role is "user" or "assistant")
            .TakeLast(MaxHistoryTurns)
            .ToList();

        foreach (var turn in historyToSend)
            messages.Add(new AiChatMessage(turn.Role, turn.Content));

        return messages;
    }

    private static string BuildInterpretationPrompt(
        string question, string sql, string resultTable, QueryResult qr, string locale)
    {
        return $@"You are a business data analyst. The user asked a question about their company data.
A SQL query was executed and produced the results below.

USER QUESTION: {question}

SQL EXECUTED:
{sql}

QUERY RESULTS ({qr.RowCount} rows{(qr.Truncated ? ", truncated" : "")}):
{resultTable}

YOUR TASK:
- Provide a clear, concise answer in {locale} language.
- Highlight key numbers, totals, and insights.
- If the data is tabular, summarize the key points rather than repeating every row.
- Use bullet points or numbered lists for clarity when appropriate.
- If there are notable patterns (top/bottom performers, trends), mention them.
- Do NOT include SQL in your answer.
- Keep the answer focused and business-relevant.";
    }

    private static string? ExtractSql(string aiResponse)
    {
        // Look for ```sql ... ``` blocks
        var sqlBlockStart = aiResponse.IndexOf("```sql", StringComparison.OrdinalIgnoreCase);
        if (sqlBlockStart >= 0)
        {
            var codeStart = aiResponse.IndexOf('\n', sqlBlockStart);
            if (codeStart < 0) return null;
            var codeEnd = aiResponse.IndexOf("```", codeStart + 1, StringComparison.Ordinal);
            if (codeEnd < 0) return null;
            return aiResponse[(codeStart + 1)..codeEnd].Trim();
        }

        // Look for ``` ... ``` blocks (no language tag)
        var genericStart = aiResponse.IndexOf("```", StringComparison.Ordinal);
        if (genericStart >= 0)
        {
            var codeStart = aiResponse.IndexOf('\n', genericStart);
            if (codeStart < 0) return null;
            var codeEnd = aiResponse.IndexOf("```", codeStart + 1, StringComparison.Ordinal);
            if (codeEnd < 0) return null;
            var candidate = aiResponse[(codeStart + 1)..codeEnd].Trim();
            if (candidate.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static string CleanAnswer(string text)
    {
        // Remove SQL code blocks from the answer if present (they're shown separately)
        var result = System.Text.RegularExpressions.Regex.Replace(
            text, @"```sql[\s\S]*?```", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"```[\s\S]*?```", "");
        return result.Trim();
    }
}
