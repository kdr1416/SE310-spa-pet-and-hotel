using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;

namespace PetSpa.Api.Services.Scheduling;

internal static class EmployeeScheduleLock
{
    internal static Task AcquireAsync(PetSpaDbContext db, int employeeId, CancellationToken ct) =>
        db.Database.IsRelational()
            ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x53434844}, {employeeId})", ct)
            : Task.CompletedTask;
}
