using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PetSpa.Api.Data;

/// <summary>Builds migrations without reading credentials or opening a database connection.</summary>
public sealed class PetSpaDbContextFactory : IDesignTimeDbContextFactory<PetSpaDbContext>
{
    public PetSpaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PetSpaDbContext>().UseNpgsql().Options;
        return new PetSpaDbContext(options);
    }
}
