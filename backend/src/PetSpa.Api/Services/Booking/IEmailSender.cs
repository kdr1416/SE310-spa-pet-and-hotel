namespace PetSpa.Api.Services.Booking;

public interface IEmailSender
{
    Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken);
    Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken);
}
