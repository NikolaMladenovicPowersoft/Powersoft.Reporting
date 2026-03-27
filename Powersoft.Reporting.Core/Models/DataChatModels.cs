namespace Powersoft.Reporting.Core.Models;

public class DataChatRequest
{
    public string Message { get; set; } = "";
    public List<DataChatTurn> History { get; set; } = new();
    public string? Locale { get; set; } = "en";
}

public class DataChatTurn
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class DataChatResponse
{
    public bool Success { get; set; }
    public string Answer { get; set; } = "";
    public string? Sql { get; set; }
    public List<string>? Columns { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
    public string? ErrorMessage { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double DurationMs { get; set; }
}
