using System.Net;
using System.Net.Mail;
using FransStudios.Api.Configuration;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Infrastructure;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? ReplyTo = null);

/// <summary>
/// Email is optional in V1. Provider "None" only logs; "Smtp" sends via any SMTP relay
/// (Gmail with an app password, Microsoft 365, Brevo, Postmark, SES, …). Another transactional
/// provider can be added by implementing this interface and registering it in Program.cs.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email provider is 'None'. Would send '{Subject}' to {To}.", message.Subject, message.To);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(o.FromAddress ?? throw new InvalidOperationException("Email:FromAddress is not set."), o.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false,
        };
        mail.To.Add(message.To);
        if (message.ReplyTo is not null) mail.ReplyToList.Add(message.ReplyTo);

        using var smtp = new SmtpClient(o.Smtp.Host, o.Smtp.Port)
        {
            EnableSsl = o.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = o.Smtp.Username is null ? null : new NetworkCredential(o.Smtp.Username, o.Smtp.Password),
        };
        await smtp.SendMailAsync(mail, ct);
    }
}

/// <summary>Sends email off the request path so a slow or failing provider never breaks a form submission.</summary>
public sealed class EmailDispatcher(IEmailSender sender, ILogger<EmailDispatcher> logger, IHostApplicationLifetime lifetime)
{
    public void Enqueue(EmailMessage message) => _ = Task.Run(async () =>
    {
        try { await sender.SendAsync(message, lifetime.ApplicationStopping); }
        catch (Exception ex) { logger.LogError(ex, "Failed to send email '{Subject}'", message.Subject); }
    });
}
