using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Powersoft.Reporting.Data.Tenant;

/// <summary>
/// Executes AI-generated SQL queries safely: validates read-only, caps rows,
/// enforces timeout, and runs inside a rolled-back transaction.
/// </summary>
public class SafeQueryExecutor
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "CREATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY", "MERGE",
        "BACKUP", "RESTORE", "SHUTDOWN", "RECONFIGURE",
        "OPENROWSET", "OPENDATASOURCE", "OPENQUERY"
    };

    private static readonly Regex ForbiddenPatterns = new(
        @"\b(xp_|sp_|fn_|DBCC\s|BULK\s|INTO\s+\w+\s+FROM|WAITFOR\s)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxRows = 500;
    private const int TimeoutSeconds = 30;

    public static (bool isValid, string? error) ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "Empty SQL query.");

        var trimmed = sql.Trim().TrimEnd(';').Trim();

        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return (false, "Only SELECT queries are allowed.");

        var upper = trimmed.ToUpperInvariant();
        foreach (var kw in ForbiddenKeywords)
        {
            if (Regex.IsMatch(upper, $@"\b{kw}\b"))
                return (false, $"Forbidden keyword detected: {kw}");
        }

        if (ForbiddenPatterns.IsMatch(trimmed))
            return (false, "Forbidden pattern detected in query.");

        if (upper.Contains("INTO") && upper.Contains("FROM"))
        {
            if (Regex.IsMatch(upper, @"\bSELECT\b.*\bINTO\b\s+\w+.*\bFROM\b"))
                return (false, "SELECT INTO is not allowed.");
        }

        return (true, null);
    }

    public static async Task<QueryResult> ExecuteAsync(string connectionString, string sql, CancellationToken ct = default)
    {
        var (valid, error) = ValidateSql(sql);
        if (!valid)
            return QueryResult.Error(error!);

        var safeSql = $"SET ROWCOUNT {MaxRows};\n{sql.Trim().TrimEnd(';')}";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadUncommitted, ct);

        try
        {
            await using var cmd = new SqlCommand(safeSql, conn, tx)
            {
                CommandTimeout = TimeoutSeconds
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[columns[i]] = val == DBNull.Value ? null : val;
                }
                rows.Add(row);
            }

            await tx.RollbackAsync(ct);
            return new QueryResult(true, null, columns, rows, rows.Count >= MaxRows);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            return QueryResult.Error($"Query timed out after {TimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            return QueryResult.Error($"Query execution failed: {ex.Message}");
        }
    }
}

public class QueryResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> Columns { get; init; } = new();
    public List<Dictionary<string, object?>> Rows { get; init; } = new();
    public bool Truncated { get; init; }
    public int RowCount => Rows.Count;

    public QueryResult(bool success, string? error, List<string> columns,
        List<Dictionary<string, object?>> rows, bool truncated)
    {
        Success = success;
        ErrorMessage = error;
        Columns = columns;
        Rows = rows;
        Truncated = truncated;
    }

    public static QueryResult Error(string message) =>
        new(false, message, new(), new(), false);

    /// <summary>
    /// Returns the result as a compact text table for AI interpretation.
    /// </summary>
    public string ToTextTable(int maxRows = 50)
    {
        if (!Success || Columns.Count == 0 || Rows.Count == 0)
            return Success ? "(no results)" : $"ERROR: {ErrorMessage}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", Columns));
        sb.AppendLine(new string('-', Math.Min(Columns.Count * 15, 120)));

        var display = Rows.Take(maxRows);
        foreach (var row in display)
        {
            var vals = Columns.Select(c =>
            {
                var v = row.GetValueOrDefault(c);
                if (v == null) return "NULL";
                if (v is decimal d) return d.ToString("N2");
                if (v is DateTime dt) return dt.ToString("yyyy-MM-dd");
                return v.ToString() ?? "";
            });
            sb.AppendLine(string.Join(" | ", vals));
        }

        if (Rows.Count > maxRows)
            sb.AppendLine($"... ({Rows.Count - maxRows} more rows)");
        if (Truncated)
            sb.AppendLine($"[Results capped at {Rows.Count} rows]");

        return sb.ToString();
    }
}
