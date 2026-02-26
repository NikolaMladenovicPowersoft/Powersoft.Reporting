using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services;

public sealed class BrevoSmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _opt;
    private readonly ILogger<BrevoSmtpEmailSender> _logger;

    public BrevoSmtpEmailSender(IOptions<EmailOptions> opt, ILogger<BrevoSmtpEmailSender> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
        => SendAsync(toEmail, subject, htmlBody, textBody, attachments: null, ct);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string textBody,
        IEnumerable<EmailAttachment>? attachments, CancellationToken ct = default)
    {
        using var msg = new MailMessage();
        msg.From = new MailAddress(_opt.FromEmail, _opt.FromName);
        msg.To.Add(toEmail);
        msg.Subject = subject;
        msg.Body = htmlBody;
        msg.IsBodyHtml = true;

        var plainView = AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain");
        msg.AlternateViews.Add(plainView);

        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                var stream = new MemoryStream(att.Content);
                var attachment = new Attachment(stream, att.FileName, att.ContentType);
                msg.Attachments.Add(attachment);
            }
        }

        using var client = new SmtpClient(_opt.SmtpHost, _opt.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_opt.SmtpUser, _opt.SmtpPassword)
        };

        _logger.LogInformation("Sending email to {To}, subject: {Subject}", toEmail, subject);
        await client.SendMailAsync(msg, ct);
        _logger.LogInformation("Email sent successfully to {To}", toEmail);
    }
}
