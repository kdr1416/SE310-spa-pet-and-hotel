using System.Net;
using System.Net.Mail;

namespace PetSpa.Api.Services.Booking;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken)
    {
        var smtp = configuration.GetSection("Smtp");
        var host = smtp["Host"];
        var from = smtp["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Thiếu cấu hình SMTP.");

        using var message = new MailMessage(from, recipient, "Mã tra cứu lịch PetSpa",
            $"Mã tra cứu lịch hẹn của bạn là {trackingCode}. Vui lòng giữ mã để tra cứu lịch trên PetSpa.");
        using var client = new SmtpClient(host, smtp.GetValue("Port", 587))
        {
            EnableSsl = smtp.GetValue("EnableSsl", true)
        };
        if (smtp["Username"] is { Length: > 0 } username)
            client.Credentials = new NetworkCredential(username, smtp["Password"]);

        await client.SendMailAsync(message, cancellationToken);
    }
}
