namespace AttendanceClock.Api.Domain;

/// <summary>
/// An employee asks a manager to fix the times of one of their FINISHED shifts (Plan 13, PRD N3).
/// The requested times are only a suggestion: they never touch the shift directly. On approval the server runs the
/// normal manager correction (Plan 10), so the change is validated, labelled ManualCorrection and audited (PRD rule 2).
/// Requests are never deleted: approved and rejected ones stay as history.
/// </summary>
public class CorrectionRequest
{
    public long Id { get; set; }

    public long ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;

    /// <summary>Who asked: always the shift's own employee.</summary>
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    /// <summary>The Clock In the employee says is right; null = keep the recorded one.</summary>
    public DateTimeOffset? RequestedClockIn { get; set; }

    /// <summary>The Clock Out the employee says is right; null = keep the recorded one.</summary>
    public DateTimeOffset? RequestedClockOut { get; set; }

    public required string Reason { get; set; }

    public CorrectionRequestStatus Status { get; set; } = CorrectionRequestStatus.Pending;

    /// <summary>When the request was sent, from the trusted clock (also used to refuse future times).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The manager who approved or rejected it; null while pending.</summary>
    public int? DecidedByEmployeeId { get; set; }
    public Employee? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Changes on every update, so two managers deciding at once can't both win.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Stored as text ("Pending" / "Approved" / "Rejected") so it reads clearly in SQL.</summary>
public enum CorrectionRequestStatus
{
    Pending,
    Approved,
    Rejected,
}
