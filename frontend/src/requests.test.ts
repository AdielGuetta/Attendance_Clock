import { describe, expect, it } from "vitest";
import type { CorrectionRequest, Shift } from "./api";
import { buildRequest, canRequestFix, fromInput, fromServer, latestByShift, summarizeChange, summarizeRequest } from "./requests";
import { formatDelta, zurichWallToInstant } from "./time";

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

  it("shows the new length and the change, and spots a moved day", () => {
    // Recorded Tue 17 Nov 08:00 → 16:30 (8:30). Requested Clock Out 17:00: same day, 9:00, +0:30.
    const recIn = fromServer("2026-11-17T08:00:00+01:00");
    const recOut = fromServer("2026-11-17T16:30:00+01:00");
    const later = summarizeChange(recIn, recOut, 510, recIn, fromInput("2026-11-17T17:00"));
    expect(later).toMatchObject({ minutes: 540, deltaMinutes: 30, overLimit: false, inDayChanged: false, outDayChanged: false, valid: true });

    // Typo: Clock In on Mon 16 Nov instead of Tue 17 Nov → 32:30, day changed, over 14 h.
    const typo = summarizeChange(recIn, recOut, 510, fromInput("2026-11-16T08:00"), recOut);
    expect(typo).toMatchObject({ minutes: 1950, inDayChanged: true, overLimit: true, valid: true });
    expect(formatDelta(typo.deltaMinutes)).toBe("+24:00");
  });

  it("flags only MORE than 14 h (like the server), and refuses Clock Out before Clock In", () => {
    const recIn = fromServer("2026-11-17T08:00:00+01:00");
    const recOut = fromServer("2026-11-17T16:00:00+01:00");
    expect(summarizeChange(recIn, recOut, 480, recIn, fromInput("2026-11-17T22:00")).overLimit).toBe(false); // exactly 14:00
    expect(summarizeChange(recIn, recOut, 480, recIn, fromInput("2026-11-17T22:01")).overLimit).toBe(true);
    expect(summarizeChange(recIn, recOut, 480, recIn, fromInput("2026-11-17T07:00")).valid).toBe(false);
  });

  it("reads typed times with Zurich's summer/winter offset, so a night when the clocks change comes out right", () => {
    expect(zurichWallToInstant("2026-11-17T08:00")).toBe(Date.parse("2026-11-17T08:00:00+01:00")); // winter
    expect(zurichWallToInstant("2026-07-15T08:00")).toBe(Date.parse("2026-07-15T08:00:00+02:00")); // summer
    // 28 Mar 2027: 22:00 → 06:00 on the wall is 7 real hours (the clocks jump forward at 02:00).
    const night = summarizeChange(fromServer("2027-03-27T22:00:00+01:00"), fromServer("2027-03-28T05:00:00+02:00"), 360,
      fromServer("2027-03-27T22:00:00+01:00"), fromInput("2027-03-28T06:00"));
    expect(night.minutes).toBe(420);
  });

  it("summarizes a stored request for the manager, using the recorded time where nothing was requested", () => {
    const r = { ...request(1, 1, "Pending"), requestedClockIn: "2026-11-17T08:00:00+01:00", requestedClockOut: null };
    const s = summarizeRequest(r); // shift(): Wed 18 Nov 08:00:12 → 19:00:41, 660 min
    expect(s.inDayChanged).toBe(true);  // 18 → 17 Nov
    expect(s.minutes).toBe(2101);       // 35 h 01 min
    expect(s.overLimit).toBe(true);
  });

  it("keeps the newest request per shift (the server sends newest first)", () => {
    const latest = latestByShift([request(9, 1, "Pending"), request(5, 1, "Rejected"), request(4, 2, "Approved")]);
    expect(latest.get(1)?.id).toBe(9);
    expect(latest.get(2)?.id).toBe(4);
  });
});
