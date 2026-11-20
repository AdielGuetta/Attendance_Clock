import { describe, expect, it } from "vitest";
import type { History, Shift } from "./api";
import { pickRecent, recentRange } from "./recent";

const shift = (id: number, clockIn: string, clockOut: string | null = "2026-11-20T17:00:00+01:00"): Shift => ({
  id, zurichDate: clockIn.slice(0, 10), clockIn, clockInSource: "timeapi.io",
  clockOut, clockOutSource: clockOut ? "timeapi.io" : null, durationMinutes: clockOut ? 60 : null, flags: [],
  workLocation: "Office",
});

const history = (shifts: Shift[], openShift: Shift | null = null): History =>
  ({ from: "2026-11-07", to: "2026-11-20", totalMinutes: 0, days: [], shifts, openShift });

describe("recent shifts (proj2 warm-up)", () => {
  it("asks for the last 14 days ending today, across a month boundary", () => {
    expect(recentRange("2026-12-03")).toEqual({ from: "2026-11-20", to: "2026-12-03" });
  });

  it("shows the newest shifts first and at most 5", () => {
    const shifts = [1, 2, 3, 4, 5, 6, 7].map((d) => shift(d, `2026-11-1${d}T08:00:00+01:00`)); // oldest first, like the server
    expect(pickRecent(history(shifts)).map((s) => s.id)).toEqual([7, 6, 5, 4, 3]);
  });

  it("puts a shift that's still in progress at the top", () => {
    const open = shift(99, "2026-11-20T08:00:00+01:00", null);
    const result = pickRecent(history([shift(1, "2026-11-19T08:00:00+01:00")], open));
    expect(result.map((s) => s.id)).toEqual([99, 1]);
  });

  it("returns an empty list when there's nothing yet", () => {
    expect(pickRecent(history([]))).toEqual([]);
  });
});
