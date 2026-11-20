import { describe, expect, it } from "vitest";
import type { CorrectionRequest, Shift } from "./api";
import { buildRequest, canRequestFix, latestByShift } from "./requests";

const shift = (clockOut: string | null = "2026-11-18T19:00:41.5+01:00"): Shift => ({
  id: 1, zurichDate: "2026-11-18", clockIn: "2026-11-18T08:00:12+01:00", clockInSource: "timeapi.io",
  clockOut, clockOutSource: clockOut ? "timeapi.io" : null, durationMinutes: clockOut ? 660 : null, flags: [], workLocation: "Office",
});

const request = (id: number, shiftId: number, status: CorrectionRequest["status"]): CorrectionRequest => ({
  id, shiftId, employeeId: 2, employeeName: "Dana Cohen", status, requestedClockIn: null,
  requestedClockOut: "2026-11-18T17:00:00+01:00", reason: "Forgot to clock out", createdAt: "2026-11-20T18:00:00+01:00",
  decidedBy: null, decidedAt: null, shift: shift(),
});

describe("correction requests (Plan 13)", () => {
  it("sends only the times that changed", () => {
    expect(buildRequest(shift(), "2026-11-18T08:00", "2026-11-18T17:00", "  Forgot to clock out  "))
      .toEqual({ clockOut: "2026-11-18T17:00", reason: "Forgot to clock out" });
    expect(buildRequest(shift(), "2026-11-18T07:30", "2026-11-18T19:00", "Came in early"))
      .toEqual({ clockIn: "2026-11-18T07:30", reason: "Came in early" });
  });

  it("allows a fix only on a finished shift without a waiting request", () => {
    expect(canRequestFix(shift(), undefined)).toBe(true);
    expect(canRequestFix(shift(null), undefined)).toBe(false); // still open (user's decision)
    expect(canRequestFix(shift(), request(1, 1, "Pending"))).toBe(false);
    expect(canRequestFix(shift(), request(1, 1, "Rejected"))).toBe(true); // may ask again
  });

  it("keeps the newest request per shift (the server sends newest first)", () => {
    const latest = latestByShift([request(9, 1, "Pending"), request(5, 1, "Rejected"), request(4, 2, "Approved")]);
    expect(latest.get(1)?.id).toBe(9);
    expect(latest.get(2)?.id).toBe(4);
  });
});
