using AttendanceClock.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Data;

/// <summary>
/// The app's connection to the database. Each DbSet is one table.
/// The exact table rules (lengths, indexes, constraints) live in the Configurations folder.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftAudit> ShiftAudits => Set<ShiftAudit>();
    public DbSet<CorrectionRequest> CorrectionRequests => Set<CorrectionRequest>(); // Plan 13

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration class in this project.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
