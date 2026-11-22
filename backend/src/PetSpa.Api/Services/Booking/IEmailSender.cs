namespace PetSpa.Api.Services.Booking;

public interface IEmailSender
{
    Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken);
    Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken);
    Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct);
    Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount,
        DateTimeOffset refundedAt, CancellationToken ct);
}
