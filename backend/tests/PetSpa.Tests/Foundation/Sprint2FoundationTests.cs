using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PetSpa.Api.Data;
using PetSpa.Api.Services;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;
using PetSpa.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PetSpa.Tests.Foundation;

public sealed class Sprint2FoundationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Host_ResolvesAllFoundationServices_AndTestingDefaultWorkerIsDisabled()
    {
        var probe = new ProbeState();
        using var host = AuthApplicationFactory.Create().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IDepositExpiryService>(_ => new ScopedProbe(probe)))));
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HotelCapacityCalculator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDepositExpiryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBillingSettlementHelper>());
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<DepositExpiryBackgroundService>());
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Testing_ExplicitTrueEnablesWorker()
    {
        var probe = new ProbeState();
        using var host = AuthApplicationFactory.Create().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["DepositExpiry:BackgroundEnabled"] = "true" }));
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Scoped<IDepositExpiryService>(_ => new ScopedProbe(probe))));
        });
        using var client = host.CreateClient();
        await probe.First.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Worker_DisposesScope_ContinuesAfterFailedSweep_AndStopsOnCancellation()
    {
        var clock = new ManualClock();
        var state = new ProbeState { FailFirst = true };
        await using var services = new ServiceCollection().AddScoped<IDepositExpiryService>(_ => new ScopedProbe(state))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = new DepositExpiryBackgroundService(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), clock, NullLogger<DepositExpiryBackgroundService>.Instance);
        await worker.StartAsync(default);
        await clock.TickAsync();
        await state.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, state.Calls);
        Assert.Equal(2, state.Disposed);
        Assert.Equal(1, state.MaxActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Worker_RejectsNonPositiveIntervals(int interval)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DepositExpiry:IntervalSeconds"] = interval.ToString() }).Build();
        Assert.Throws<InvalidOperationException>(() => new DepositExpiryBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(), config, TimeProvider.System,
            NullLogger<DepositExpiryBackgroundService>.Instance));
    }

    [Fact]
    public async Task Worker_DrainsTwentyOneBookings_WhenCurrentExtensionIsOff()
    {
        var now = BookingDepositProjectionTests.Now;
        await using var fixture = await FoundationTestSupport.CreateAsync(now);
        await using (var db = fixture.Database.CreateContext())
        {
            var slot = await db.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync();
            slot.TongSlot = 100;
            slot.TranPhut = 10000;
            await db.SaveChangesAsync();
            for (var i = 0; i < 21; i++) await fixture.AddBookingAsync(db);
        }
        var finished = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = new ServiceCollection()
            .AddDbContext<PetSpaDbContext>(options => options.UseNpgsql(fixture.Database.ConnectionString))
            .AddScoped<IDepositExpiryService>(provider => new SignallingExpiry(new DepositExpiryService(
                provider.GetRequiredService<PetSpaDbContext>(), provider.GetRequiredService<DbContextOptions<PetSpaDbContext>>(), fixture.Clock), finished))
            .BuildServiceProvider();
        using var worker = new DepositExpiryBackgroundService(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), fixture.Clock, NullLogger<DepositExpiryBackgroundService>.Instance);
        var elapsed = Stopwatch.StartNew();
        await worker.StartAsync(default);
        Assert.Equal(21, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        output.WriteLine("Global expiry 21 bookings / batches20+1: {0} ms (local fixture, not an NFR certificate).", elapsed.ElapsedMilliseconds);
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(21, await verify.DonDatLichSet.CountAsync(x => x.TrangThai == BookingState.CANCELLED));
        Assert.Equal(0, (await verify.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync()).SlotDaDat);
    }

    private sealed class SignallingExpiry(IDepositExpiryService inner, TaskCompletionSource<int> finished) : IDepositExpiryService
    {
        public async Task<int> ExpireOverdueDepositsAsync(DateTimeOffset now, int? targetBookingId, CancellationToken ct,
            IReadOnlyCollection<int>? targetKhungGioIds = null, IReadOnlyCollection<int>? targetRoomTypeIds = null)
        {
            var count = await inner.ExpireOverdueDepositsAsync(now, targetBookingId, ct, targetKhungGioIds, targetRoomTypeIds);
            finished.TrySetResult(count);
            return count;
        }
        public Task ExpireLockedBookingDepositAsync(DonDatLich booking, DateTimeOffset now, CancellationToken ct) =>
            inner.ExpireLockedBookingDepositAsync(booking, now, ct);
    }

    private sealed class ProbeState
    {
        public int Calls;
        public int Disposed;
        public int Active;
        public int MaxActive;
        public bool FailFirst;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ScopedProbe(ProbeState state) : IDepositExpiryService, IDisposable
    {
        public Task<int> ExpireOverdueDepositsAsync(DateTimeOffset now, int? targetBookingId, CancellationToken ct,
            IReadOnlyCollection<int>? targetKhungGioIds = null, IReadOnlyCollection<int>? targetRoomTypeIds = null)
        {
            Assert.Null(targetBookingId);
            Assert.Null(targetKhungGioIds);
            Assert.Null(targetRoomTypeIds);
            var active = Interlocked.Increment(ref state.Active);
            state.MaxActive = Math.Max(state.MaxActive, active);
            try
            {
                var call = Interlocked.Increment(ref state.Calls);
                (call == 1 ? state.First : state.Second).TrySetResult();
                if (call == 1 && state.FailFirst) throw new InvalidOperationException("Test failure");
                return Task.FromResult(0);
            }
            finally { Interlocked.Decrement(ref state.Active); }
        }
        public Task ExpireLockedBookingDepositAsync(DonDatLich booking, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public void Dispose() => Interlocked.Increment(ref state.Disposed);
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly Channel<Timer> _timers = Channel.CreateUnbounded<Timer>();
        private DateTimeOffset _now = BookingDepositProjectionTests.Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, dueTime);
            _timers.Writer.TryWrite(timer);
            return timer;
        }
        public async Task TickAsync()
        {
            var timer = await _timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            _now += timer.Due;
            timer.Fire();
        }
        private sealed class Timer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due { get; private set; } = due;
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !_disposed; }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
