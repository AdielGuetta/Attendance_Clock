using AttendanceClock.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceClock.Api.Data.Configurations;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> s)
    {
        s.ToTable(t =>
        {
            // The database itself refuses a Clock Out that isn't after the Clock In (PRD EC-9).
            t.HasCheckConstraint("CK_Shifts_ClockOutAfterClockIn", "[ClockOut] IS NULL OR [ClockOut] > [ClockIn]");
            // Only the known locations, even if someone writes SQL by hand (Plan 12). NULL = shift from before the feature.
            t.HasCheckConstraint("CK_Shifts_WorkLocation", "[WorkLocation] IS NULL OR [WorkLocation] IN ('Office', 'Remote')");
        });

        s.Property(x => x.ClockInSource).HasMaxLength(32).IsUnicode(false);
        s.Property(x => x.ClockOutSource).HasMaxLength(32).IsUnicode(false);
        s.Property(x => x.WorkLocation).HasConversion<string>().HasMaxLength(16).IsUnicode(false);

        s.Property(x => x.RowVersion).IsRowVersion();

        s.HasOne(x => x.Employee)
            .WithMany(e => e.Shifts)
            .HasForeignKey(x => x.EmployeeId)
            // Never delete an employee's shift history by accident. Employees are deactivated, not deleted.
            .OnDelete(DeleteBehavior.Restrict);

        // KEY RULE (PRD D-9): at most ONE open shift per employee, enforced by the database.
        // Even if two Clock In requests arrive at the same instant, only one can succeed.
        s.HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasFilter("[ClockOut] IS NULL")
            .HasDatabaseName("UX_Shifts_OneOpenShiftPerEmployee");

        // Makes "this employee's shifts in a date range" fast (history screens).
        s.HasIndex(x => new { x.EmployeeId, x.ClockIn });
    }
}
