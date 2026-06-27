using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Data;

/// <summary>Dedicated store for ASP.NET Core Data Protection keys (no tenant filters).</summary>
public sealed class DataProtectionDbContext : DbContext, IDataProtectionKeyContext
{
    public DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DataProtectionKey>(e => e.ToTable("DataProtectionKeys"));
    }
}
