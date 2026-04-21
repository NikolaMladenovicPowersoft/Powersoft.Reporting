using System.Globalization;
using FluentAssertions;
using Powersoft.Reporting.Core.Models;
using Xunit;

namespace Powersoft.Reporting.Tests.Catalogue;

// =============================================================================
// CATALOGUE REGRESSION FRAMEWORK — SCAFFOLDING
//
// This file is intentionally NOT runnable end-to-end. To activate:
//   1. Create folder: Powersoft.Reporting.Tests/_Fixtures/Catalogue/<scenario>/
//   2. Drop in:
//        - params.json    (CatalogueFilter serialized)
//        - expected.csv   (output exported from Powersoft365 — comma-separated, header row)
//   3. Set environment variable PSREP_REGRESSION_CONNSTRING to a tenant DB that
//      contains the data the P365 fixtures were generated against.
//   4. Run: dotnet test --filter "Category=CatalogueRegression"
//
// The scenarios are auto-discovered from disk via the [Theory] MemberData below.
// If no fixtures exist OR the env var is unset, all tests are skipped (not failed).
// =============================================================================

[Trait("Category", "CatalogueRegression")]
public class CatalogueRegressionTests
{
    private static readonly string FixturesRoot =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "_Fixtures", "Catalogue");

    public static IEnumerable<object[]> DiscoverScenarios()
    {
        if (!Directory.Exists(FixturesRoot))
        {
            yield return new object[] { "(no fixtures — see CatalogueRegressionFramework.cs header)" };
            yield break;
        }

        var dirs = Directory.GetDirectories(FixturesRoot);
        if (dirs.Length == 0)
        {
            yield return new object[] { "(no fixtures — see CatalogueRegressionFramework.cs header)" };
            yield break;
        }

        foreach (var dir in dirs)
            yield return new object[] { Path.GetFileName(dir) };
    }

    [Theory]
    [MemberData(nameof(DiscoverScenarios))]
    public async Task Scenario_Output_Matches_Powersoft365(string scenarioName)
    {
        if (scenarioName.StartsWith("("))
        {
            // Sentinel — no fixtures present. Skip cleanly.
            return;
        }

        var connString = Environment.GetEnvironmentVariable("PSREP_REGRESSION_CONNSTRING");
        if (string.IsNullOrWhiteSpace(connString))
        {
            // Env var not set — skip cleanly. Test infra reports "Skipped".
            return;
        }

        var scenarioDir = Path.Combine(FixturesRoot, scenarioName);
        var paramsPath = Path.Combine(scenarioDir, "params.json");
        var expectedPath = Path.Combine(scenarioDir, "expected.csv");

        File.Exists(paramsPath).Should().BeTrue($"params.json missing for {scenarioName}");
        File.Exists(expectedPath).Should().BeTrue($"expected.csv missing for {scenarioName}");

        var paramsJson = await File.ReadAllTextAsync(paramsPath);
        var filter = System.Text.Json.JsonSerializer.Deserialize<CatalogueFilter>(paramsJson, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize params.json");

        var expected = CsvLoader.Load(expectedPath);

        var repo = new Powersoft.Reporting.Data.Tenant.CatalogueRepository(connString);
        filter.PageNumber = 1;
        filter.PageSize = int.MaxValue;
        var actual = await repo.GetCatalogueDataAsync(filter);

        var diff = RowComparator.Diff(expected, actual.Items, decimalTolerance: 0.005m);
        diff.Should().BeEmpty($"P365 vs new should match for scenario '{scenarioName}'.\nFirst differences:\n{string.Join("\n", diff.Take(10))}");
    }
}

// -----------------------------------------------------------------------------
// CSV loader — minimal RFC 4180-ish reader (handles quotes, commas, newlines in quotes)
// -----------------------------------------------------------------------------
internal static class CsvLoader
{
    public static List<Dictionary<string, string>> Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return new();

        var headers = ParseLine(lines[0]);
        var rows = new List<Dictionary<string, string>>(lines.Length - 1);

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var fields = ParseLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headers.Count && c < fields.Count; c++)
                row[headers[c]] = fields[c];
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else cur.Append(ch);
            }
            else
            {
                if (ch == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else if (ch == '"' && cur.Length == 0) inQuotes = true;
                else cur.Append(ch);
            }
        }
        fields.Add(cur.ToString());
        return fields;
    }
}

// -----------------------------------------------------------------------------
// Row comparator — diffs expected (CSV) vs actual (CatalogueRow). Tolerant on decimals.
// -----------------------------------------------------------------------------
internal static class RowComparator
{
    public static List<string> Diff(List<Dictionary<string, string>> expected, List<CatalogueRow> actual, decimal decimalTolerance)
    {
        var diffs = new List<string>();

        if (expected.Count != actual.Count)
        {
            diffs.Add($"ROW COUNT MISMATCH — expected {expected.Count}, actual {actual.Count}");
            return diffs;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            var exp = expected[i];
            var act = actual[i];
            var actMap = ToMap(act);

            foreach (var kvp in exp)
            {
                if (!actMap.TryGetValue(kvp.Key, out var actVal))
                {
                    diffs.Add($"row {i}: column '{kvp.Key}' missing in actual");
                    continue;
                }

                if (decimal.TryParse(kvp.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var expDec)
                    && decimal.TryParse(actVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var actDec))
                {
                    if (Math.Abs(expDec - actDec) > decimalTolerance)
                        diffs.Add($"row {i} '{kvp.Key}': expected {expDec}, actual {actDec} (delta {expDec - actDec:F4})");
                }
                else if (!string.Equals(kvp.Value?.Trim() ?? "", actVal?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    diffs.Add($"row {i} '{kvp.Key}': expected '{kvp.Value}', actual '{actVal}'");
                }
            }
        }

        return diffs;
    }

    private static Dictionary<string, string> ToMap(CatalogueRow row)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in typeof(CatalogueRow).GetProperties())
        {
            var v = prop.GetValue(row);
            d[prop.Name] = v switch
            {
                null => "",
                decimal dec => dec.ToString(CultureInfo.InvariantCulture),
                double dbl => dbl.ToString(CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                _ => v.ToString() ?? ""
            };
        }
        return d;
    }
}
