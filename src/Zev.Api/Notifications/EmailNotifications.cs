using System.Net;
using System.Net.Mail;
using System.Threading.Channels;

namespace Zev.Api.Notifications;

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    // False when the message was not delivered (no SMTP configured); exceptions mean a failed delivery attempt.
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

// SMTP from configuration: Smtp:Host (required), Smtp:Port (25), Smtp:From, Smtp:User, Smtp:Password, Smtp:EnableSsl.
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Smtp");
        using var client = new SmtpClient(section["Host"], section.GetValue("Port", 25))
        {
            EnableSsl = section.GetValue("EnableSsl", true),
            Credentials = string.IsNullOrEmpty(section["User"]) ? null : new NetworkCredential(section["User"], section["Password"])
        };
        using var mail = new MailMessage(section["From"] ?? "zev-platforma@localhost", message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, cancellationToken);
        return true;
    }
}

// Without SMTP the message is written to the application log instead, so nothing fails.
public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("SMTP is not configured; e-mail to {To} not sent. Subject: {Subject}", message.To, message.Subject);
        return Task.FromResult(false);
    }
}

// Requests only enqueue; a background service delivers, so a slow or unreachable mail server never delays or breaks
// the API call that triggered the e-mail.
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public bool Enqueue(EmailMessage message) => channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try { await sender.SendAsync(message, stoppingToken); }
            catch (Exception exception) when (exception is SmtpException or InvalidOperationException or IOException
                or System.Security.Authentication.AuthenticationException)
            {
                logger.LogWarning(exception, "E-mail to {To} could not be sent. Subject: {Subject}", message.To, message.Subject);
            }
        }
    }
}
