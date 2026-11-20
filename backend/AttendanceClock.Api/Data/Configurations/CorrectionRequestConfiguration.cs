using AttendanceClock.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceClock.Api.Data.Configurations;

/// <summary>Table rules for correction requests (Plan 13). As elsewhere, the data rules live in the database too.</summary>
public class CorrectionRequestConfiguration : IEntityTypeConfiguration<CorrectionRequest>
{
    public void Configure(EntityTypeBuilder<CorrectionRequest> r)
    {
        r.ToTable(t =>
        {
            // A request must ask for something: a new Clock In, a new Clock Out, or both.
            t.HasCheckConstraint("CK_CorrectionRequests_AsksForAChange",
                "[RequestedClockIn] IS NOT NULL OR [RequestedClockOut] IS NOT NULL");
            t.HasCheckConstraint("CK_CorrectionRequests_Status", "[Status] IN ('Pending', 'Approved', 'Rejected')");
            // Decided ⇔ has a decider and a decision time. Pending ⇔ neither.
            t.HasCheckConstraint("CK_CorrectionRequests_DecidedFields",
                "([Status] = 'Pending' AND [DecidedByEmployeeId] IS NULL AND [DecidedAt] IS NULL) OR " +
                "([Status] <> 'Pending' AND [DecidedByEmployeeId] IS NOT NULL AND [DecidedAt] IS NOT NULL)");
        });

        r.Property(x => x.Reason).HasMaxLength(300);
        r.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        r.Property(x => x.RowVersion).IsRowVersion();

        // Never lose a request together with its shift or people (shifts and employees aren't deleted anyway).
        r.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedByEmployeeId).OnDelete(DeleteBehavior.Restrict);

        // KEY RULE: at most ONE pending request per shift, enforced by the database (same idea as one open shift
        // per employee). Two contradicting requests for the same shift can't exist, even if sent at the same instant.
        r.HasIndex(x => x.ShiftId)
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("UX_CorrectionRequests_OnePendingPerShift");

        // Fast lists: "my requests" and "all pending requests".
        r.HasIndex(x => new { x.EmployeeId, x.CreatedAt });
        r.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
