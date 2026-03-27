using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Services.AI;

namespace Powersoft.Reporting.Web.Controllers;

[Authorize]
public class DataChatController : Controller
{
    private readonly DataChatService _chatService;
    private readonly ILogger<DataChatController> _logger;

    public DataChatController(DataChatService chatService, ILogger<DataChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] DataChatRequest request)
    {
        if (!_chatService.IsConfigured)
            return Json(new DataChatResponse
            {
                Success = false,
                Answer = "AI is not configured. Please set the API key in Settings."
            });

        var connString = HttpContext.Session.GetString(SessionKeys.TenantConnectionString);
        if (string.IsNullOrEmpty(connString))
            return Json(new DataChatResponse
            {
                Success = false,
                Answer = "Not connected to a database. Please select a database first."
            });

        if (string.IsNullOrWhiteSpace(request.Message))
            return Json(new DataChatResponse
            {
                Success = false,
                Answer = "Please enter a question."
            });

        var result = await _chatService.AskAsync(connString, request, HttpContext.RequestAborted);

        _logger.LogInformation(
            "DataChat: Q=\"{Question}\" Success={Success} Rows={Rows} Tokens={In}+{Out} Duration={Ms}ms",
            request.Message.Length > 80 ? request.Message[..80] + "..." : request.Message,
            result.Success, result.RowCount, result.InputTokens, result.OutputTokens,
            result.DurationMs.ToString("N0"));

        return Json(result);
    }
}
