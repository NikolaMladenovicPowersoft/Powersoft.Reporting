using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Web.Services;

namespace Powersoft.Reporting.Web.Controllers;

/// <summary>
/// Onboarding: list industry template packs and apply one to the currently connected company.
/// Applying clones the pack's reports into the tenant schedule table (see <see cref="TemplatePackService"/>).
/// </summary>
[Authorize]
public class TemplatePacksController : Controller
{
    private readonly TemplatePackService _service;
    private readonly ILogger<TemplatePacksController> _logger;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TemplatePacksController(TemplatePackService service, ILogger<TemplatePacksController> logger)
    {
        _service = service;
        _logger = logger;
    }

    private string? GetTenantConnectionString() =>
        HttpContext.Session.GetString(SessionKeys.TenantConnectionString);

    private string GetUserCode() =>
        HttpContext.Session.GetString(SessionKeys.UserCode) ?? User.Identity?.Name ?? "UNKNOWN";

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn))
            return Json(new { success = false, message = "Not connected to a database." });

        var appliedKeys = await _service.GetAppliedKeysAsync(conn);

        var packs = _service.GetPacks()
            .OrderBy(p => p.SortOrder)
            .Select(p =>
            {
                var reports = p.Items.Select(i => new
                {
                    i.ItemKey,
                    i.ReportType,
                    i.TemplateName,
                    i.RecurrenceType,
                    Applied = TemplatePackService.IsItemApplied(appliedKeys, p.PackCode, i.ItemKey)
                }).ToList();

                return new
                {
                    p.PackCode,
                    p.PackName,
                    p.IndustryTag,
                    p.Description,
                    Applied = reports.All(r => r.Applied),
                    Reports = reports
                };
            })
            .ToList();

        return Json(new { success = true, packs });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(string packCode, string recipients, string[]? selectedItems)
    {
        var conn = GetTenantConnectionString();
        if (string.IsNullOrEmpty(conn))
            return Json(new { success = false, message = "Not connected to a database." });

        if (string.IsNullOrWhiteSpace(packCode))
            return Json(new { success = false, message = "Pack code is required." });

        var (valid, invalid) = ParseAndValidateEmailList(recipients);
        if (valid.Length == 0)
            return Json(new { success = false, message = "At least one valid recipient email is required." });
        if (invalid.Length > 0)
            return Json(new { success = false, message = $"Invalid email(s): {string.Join(", ", invalid)}" });

        var selected = (selectedItems ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        if (selected.Length == 0)
            return Json(new { success = false, message = "Select at least one report to apply." });

        try
        {
            var result = await _service.ApplyPackAsync(conn, packCode, string.Join(",", valid), GetUserCode(), selected);
            return Json(new
            {
                success = result.Success,
                alreadyApplied = result.AlreadyApplied,
                createdCount = result.CreatedCount,
                skippedCount = result.SkippedCount,
                packName = result.PackName,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply template pack {Pack}", packCode);
            return Json(new { success = false, message = "Failed to apply template pack. The schedule tables may not exist yet." });
        }
    }

    private static (string[] valid, string[] invalid) ParseAndValidateEmailList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (Array.Empty<string>(), Array.Empty<string>());

        var all = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = all.Where(e => EmailRegex.IsMatch(e)).ToArray();
        var invalid = all.Where(e => !EmailRegex.IsMatch(e)).ToArray();
        return (valid, invalid);
    }
}
