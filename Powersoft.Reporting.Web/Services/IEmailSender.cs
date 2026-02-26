using System.Net.Mail;

namespace Powersoft.Reporting.Web.Services;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default);

    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody,
        IEnumerable<EmailAttachment>? attachments, CancellationToken ct = default);
}

public class EmailAttachment
{
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
