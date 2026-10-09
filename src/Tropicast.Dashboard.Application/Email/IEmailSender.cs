namespace Tropicast.Dashboard.Application.Email;

/// <summary>A transactional email. Bodies may contain one-time links: never log them.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Sends transactional email (confirmation, password reset, invitations).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
