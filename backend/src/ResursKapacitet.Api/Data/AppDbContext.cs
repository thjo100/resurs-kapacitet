using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ResursKapacitet.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<CapacityOverride> CapacityOverrides => Set<CapacityOverride>();
    public DbSet<WorkObject> Objects => Set<WorkObject>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<AllocationMonth> AllocationMonths => Set<AllocationMonth>();
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        // SQLite sparar inte DateTimeKind – alla tider lagras och läses som UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Color).HasMaxLength(16);
        });

        b.Entity<Resource>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Team).WithMany(t => t.Resources).HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CapacityOverride>(e =>
        {
            e.HasIndex(x => new { x.ResourceId, x.Year, x.Month }).IsUnique();
            e.HasOne(x => x.Resource).WithMany(r => r.CapacityOverrides).HasForeignKey(x => x.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkObject>(e =>
        {
            e.ToTable("WorkObjects");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Color).HasMaxLength(16);
        });

        b.Entity<Assignment>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasOne(x => x.Object).WithMany(o => o.Assignments).HasForeignKey(x => x.ObjectId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Status, x.Priority });
        });

        b.Entity<Allocation>(e =>
        {
            e.HasIndex(x => new { x.ResourceId, x.AssignmentId }).IsUnique();
            e.HasOne(x => x.Resource).WithMany(r => r.Allocations).HasForeignKey(x => x.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Assignment).WithMany(a => a.Allocations).HasForeignKey(x => x.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AllocationMonth>(e =>
        {
            e.HasIndex(x => new { x.AllocationId, x.Year, x.Month }).IsUnique();
            e.HasIndex(x => x.Year);
            e.HasOne(x => x.Allocation).WithMany(a => a.Months).HasForeignKey(x => x.AllocationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChangeLogEntry>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.ResourceId);
            e.HasIndex(x => x.AssignmentId);
            e.Property(x => x.User).HasMaxLength(120);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.Action).HasMaxLength(40);
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.UserName).IsUnique();
        });

        b.Entity<AppSettings>(e => e.Property(x => x.Id).ValueGeneratedNever());
    }
}
