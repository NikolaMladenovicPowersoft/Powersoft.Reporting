using System.Text.Json;
using System.Text.Json.Nodes;

namespace Powersoft.Reporting.Core.Helpers;

/// <summary>
/// Removes tenant-specific selection fields from a schedule's ParametersJson so it becomes a
/// portable template. Structural config (report mode, grouping enums, relative date range, toggles)
/// is kept; anything that references concrete records in one company's database (category IDs, store
/// codes, item IDs, customer/agent/status code lists, selected/suppressed account headers) is dropped,
/// because those IDs are meaningless in another company.
///
/// Pure and deterministic → unit-testable. Used when promoting an existing schedule into a template.
/// </summary>
public static class TemplateParametersSanitizer
{
    // Compared case-insensitively against JSON property names (camelCase and PascalCase variants).
    private static readonly HashSet<string> NonPortableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "storeCodes",
        "itemIds",
        "itemsSelectionJson",
        "itemsSelection",
        "customerCodesJson",
        "storeCodesJson",
        "statusCodesJson",
        "agentCodesJson",
        "tbSelectedAccounts",
        "tbSelectedHeaders",
        "tbSuppressedHeaders",
        "plSuppressedHeaders",
        "suppressedHeaders",
        "columnFilters",
        "catColumnFilters"
    };

    /// <summary>
    /// Returns a copy of <paramref name="parametersJson"/> with all non-portable selection fields
    /// removed. Returns the input unchanged when it is null/empty or not a JSON object.
    /// </summary>
    public static string? Strip(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return parametersJson;

        JsonNode? node;
        try { node = JsonNode.Parse(parametersJson); }
        catch (JsonException) { return parametersJson; }

        if (node is not JsonObject obj)
            return parametersJson;

        foreach (var key in obj.Select(kv => kv.Key).ToList())
        {
            if (NonPortableKeys.Contains(key))
                obj.Remove(key);
        }

        return obj.ToJsonString();
    }

    /// <summary>True if the JSON contains at least one non-portable selection field.</summary>
    public static bool HasNonPortableSelections(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return false;

        try
        {
            if (JsonNode.Parse(parametersJson) is not JsonObject obj)
                return false;
            return obj.Any(kv => NonPortableKeys.Contains(kv.Key)
                                 && kv.Value is not null
                                 && kv.Value.ToJsonString() is not "\"\"" and not "null" and not "[]");
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
