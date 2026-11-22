using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using MimeKit;
using PetSpa.Api.Services.Booking;

namespace PetSpa.Tests.Foundation;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task FourEmails_UseCorrectRecipientTrackingCodeAndVietnameseText()
    {
        await using var server = new LoopbackSmtp();
        IEmailSender sender = new SmtpEmailSender(Configuration(server.Port));
        await sender.SendTrackingCodeAsync("guest@example.test", "TRACK123", default);
        await sender.SendBookingConfirmedAsync("guest@example.test", "TRACK123", default);
        await sender.SendTrackingResendAsync("guest@example.test", "TRACK123", default);
        await sender.SendRefundConfirmedAsync("guest@example.test", "TRACK123", 12, 100000m, BookingDepositProjectionTests.Now, default);
        Assert.Equal(4, server.Messages.Count);
        var messages = server.Messages.Select(raw => MimeMessage.Load(new MemoryStream(raw))).ToArray();
        Assert.All(messages, message =>
        {
            Assert.Equal("guest@example.test", Assert.Single(message.To.Mailboxes).Address);
            Assert.Contains("TRACK123", message.TextBody);
            Assert.Contains("PetSpa", message.Subject);
        });
        Assert.Contains("tra cứu", messages[2].TextBody);
        Assert.Contains("100.000", messages[3].TextBody);
        Assert.Contains("01/01/2035", messages[3].TextBody);
        Assert.Contains("12", messages[3].TextBody);
        Assert.DoesNotContain("private/", messages[3].TextBody);
    }

    [Fact]
    public async Task SmtpFailure_PropagatesAndCannotReportSent()
    {
        await using var server = new LoopbackSmtp(reject: true);
        var sender = new SmtpEmailSender(Configuration(server.Port));
        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendTrackingCodeAsync("guest@example.test", "TRACK123", default));
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Cancellation_StopsPendingSend()
    {
        await using var server = new LoopbackSmtp(hang: true);
        var sender = new SmtpEmailSender(Configuration(server.Port));
        using var cancellation = new CancellationTokenSource();
        var send = sender.SendTrackingCodeAsync("guest@example.test", "TRACK123", cancellation.Token);
        await server.EhloSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task MissingConfigurationAndUnsupportedTlsPort_FailFast()
    {
        var empty = new ConfigurationBuilder().Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEmailSender(empty).SendTrackingCodeAsync("guest@example.test", "TRACK123", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEmailSender(Configuration(12345, ssl: true)).SendTrackingCodeAsync("guest@example.test", "TRACK123", default));
    }

    private static IConfiguration Configuration(int port, bool ssl = false) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = port.ToString(),
            ["Smtp:From"] = "clinic@example.test", ["Smtp:EnableSsl"] = ssl.ToString() }).Build();

    private sealed class LoopbackSmtp : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        private readonly bool _reject;
        private readonly bool _hang;
        public ConcurrentQueue<byte[]> Messages { get; } = new();
        public TaskCompletionSource EhloSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public LoopbackSmtp(bool reject = false, bool hang = false)
        {
            _reject = reject;
            _hang = hang;
            _listener.Start();
            _server = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
                    await writer.WriteLineAsync("220 localhost SMTP test");
                    while (await reader.ReadLineAsync(_stop.Token) is { } command)
                    {
                        if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                        {
                            EhloSeen.TrySetResult();
                            if (_hang) await Task.Delay(Timeout.Infinite, _stop.Token);
                            await writer.WriteLineAsync("250-localhost\r\n250-8BITMIME\r\n250 SIZE 1048576");
                        }
                        else if (command.StartsWith("MAIL") && _reject) await writer.WriteLineAsync("550 rejected by test");
                        else if (command == "DATA")
                        {
                            await writer.WriteLineAsync("354 send message");
                            var data = new StringBuilder();
                            while (await reader.ReadLineAsync(_stop.Token) is { } line && line != ".")
                                data.Append(line.StartsWith("..") ? line[1..] : line).Append("\r\n");
                            Messages.Enqueue(Encoding.UTF8.GetBytes(data.ToString()));
                            await writer.WriteLineAsync("250 accepted");
                        }
                        else if (command == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                        else await writer.WriteLineAsync("250 ok");
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _server;
            _stop.Dispose();
        }
    }
}
