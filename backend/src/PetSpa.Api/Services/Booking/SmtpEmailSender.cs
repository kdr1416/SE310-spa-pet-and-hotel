using System.Globalization;
using MailKit;
using MailKit.Security;
using MimeKit;

namespace PetSpa.Api.Services.Booking;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken) =>
        SendAsync(recipient, "Mã tra cứu lịch PetSpa",
            $"Mã tra cứu lịch hẹn của bạn là {trackingCode}. Vui lòng giữ mã để tra cứu lịch trên PetSpa.", cancellationToken);

    public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken) =>
        SendAsync(recipient, "PetSpa đã xác nhận lịch hẹn",
            $"PetSpa đã xác nhận lịch hẹn của bạn. Mã tra cứu lịch hẹn là {trackingCode}.", cancellationToken);

    public Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct) =>
        SendAsync(recipient, "Gửi lại mã tra cứu PetSpa",
            $"Mã tra cứu lịch hẹn PetSpa của bạn là {trackingCode}. Đây là mã hiện có của lịch hẹn.", ct);

    public Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount,
        DateTimeOffset refundedAt, CancellationToken ct)
    {
        var money = amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var time = TimeZoneInfo.ConvertTime(refundedAt, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"))
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("vi-VN"));
        return SendAsync(recipient, "PetSpa xác nhận hoàn cọc",
            $"PetSpa đã hoàn {money} VND cho lịch hẹn {trackingCode}. Mã hoàn cọc: {refundId}. Thời gian: {time} (giờ Việt Nam).", ct);
    }

    private async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var smtp = configuration.GetSection("Smtp");
        var host = smtp["Host"];
        var from = smtp["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Thiếu cấu hình SMTP.");

        var port = smtp.GetValue("Port", 587);
        var security = smtp.GetValue("EnableSsl", true) ? port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => throw new InvalidOperationException("SMTP TLS chỉ hỗ trợ cổng 465 hoặc 587 trong cấu hình hiện tại.")
        } : SecureSocketOptions.None;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 8000 };
        await client.ConnectAsync(host, port, security, timeout.Token);
        if (smtp["Username"] is { Length: > 0 } username)
            await client.AuthenticateAsync(username, smtp["Password"] ?? throw new InvalidOperationException("Thiếu cấu hình mật khẩu SMTP."), timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
