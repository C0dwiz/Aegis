using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Aegis.BotApi.Infrastructure.Mail;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@twospace.ru";
    public string FromName { get; set; } = "Twospace";
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body);
}

public sealed class SmtpMailService : IEmailSender
{
    private readonly MailOptions _options;
    private readonly ILogger<SmtpMailService> _logger;

    public SmtpMailService(IOptions<MailOptions> options, ILogger<SmtpMailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Mail is disabled. Intended recipient={Recipient}, subject={Subject}", toEmail, subject);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(toEmail);

        using var smtp = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        await smtp.SendMailAsync(message);
    }
}
