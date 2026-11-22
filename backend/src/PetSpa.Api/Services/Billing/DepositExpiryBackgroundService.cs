using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public sealed class DepositExpiryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<DepositExpiryBackgroundService> _logger;
    private readonly TimeSpan _interval;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment? _environment;

    public DepositExpiryBackgroundService(IServiceScopeFactory scopes, IConfiguration configuration,
        TimeProvider clock, ILogger<DepositExpiryBackgroundService> logger, IHostEnvironment? environment = null)
    {
        var seconds = configuration.GetValue("DepositExpiry:IntervalSeconds", 30);
        if (seconds <= 0 || (long)seconds * 1000 > uint.MaxValue - 1L)
            throw new InvalidOperationException("DepositExpiry:IntervalSeconds phải dương và nằm trong giới hạn timer.");
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(seconds);
        _configuration = configuration;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool?>("DepositExpiry:BackgroundEnabled") ?? !(_environment?.IsEnvironment("Testing") ?? false);
        if (!enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var expiry = scope.ServiceProvider.GetRequiredService<IDepositExpiryService>();
                await expiry.ExpireOverdueDepositsAsync(_clock.GetUtcNow(), null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogWarning("Deposit expiry sweep failed. FailureType: {FailureType}", exception.GetType().Name);
            }
            try { await Task.Delay(_interval, _clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
