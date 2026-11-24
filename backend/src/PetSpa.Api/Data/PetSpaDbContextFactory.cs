using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PetSpa.Api.Data;

/// <summary>Uses an explicit migration environment connection, or connectionless options for local generation. Does not connect.</summary>
public sealed class PetSpaDbContextFactory : IDesignTimeDbContextFactory<PetSpaDbContext>
{
    public PetSpaDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection");
        var builder = new DbContextOptionsBuilder<PetSpaDbContext>();
        if (string.IsNullOrWhiteSpace(connection))
            builder.UseNpgsql();
        else
            builder.UseNpgsql(connection);
        return new PetSpaDbContext(builder.Options);
    }
}
