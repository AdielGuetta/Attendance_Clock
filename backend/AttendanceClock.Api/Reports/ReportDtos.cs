namespace AttendanceClock.Api.Reports;

/// <summary>A manager report for a range of Zurich dates (Plan 15). All minutes are closed shifts only.</summary>
/// <param name="EmployeeId">The employee filter, or null for everyone.</param>
public sealed record ReportDto(
    DateOnly From,
    DateOnly To,
    int? EmployeeId,
    ReportTotalsDto Totals,
    IReadOnlyList<EmployeeReportDto> Employees,
    IReadOnlyList<DayReportDto> Days);

/// <param name="TotalMinutes">Sum of each closed shift's nearest-minute length (DECISIONS #7), like My week.</param>
/// <param name="Shifts">Closed shifts.</param>
/// <param name="EmployeesWorked">Employees with at least one shift (closed or still open) in the range.</param>
/// <param name="UnknownLocationMinutes">Shifts from before work location existed (Plan 12).</param>
/// <param name="OverLimitShifts">Closed shifts longer than 14 hours (same rule as the TooLong flag).</param>
/// <param name="OpenShifts">Shifts started in the range and still open: counted, but not in the hours.</param>
/// <param name="PendingRequests">Correction requests waiting for a manager, for shifts in the range (Plan 13).</param>
public sealed record ReportTotalsDto(
    int TotalMinutes,
    int Shifts,
    int EmployeesWorked,
    int OfficeMinutes,
    int RemoteMinutes,
    int UnknownLocationMinutes,
    int OverLimitShifts,
    int CorrectedShifts,
    int OpenShifts,
    int PendingRequests);

public sealed record EmployeeReportDto(
    int EmployeeId,
    string Name,
    int TotalMinutes,
    int Shifts,
    int OfficeMinutes,
    int RemoteMinutes,
    int UnknownLocationMinutes,
    int OverLimitShifts,
    int CorrectedShifts,
    int OpenShifts);

/// <summary>One Zurich date that has closed shifts (a shift counts on the date of its Clock In, D-10).</summary>
public sealed record DayReportDto(DateOnly Date, int TotalMinutes, int Shifts);
