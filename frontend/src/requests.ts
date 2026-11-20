// Logic for correction requests (Plan 13), kept apart from the screens so it's easy to test.
import type { CorrectionRequest, NewCorrectionRequest, RequestStatus, Shift } from "./api";
import { parseServerTime, TOO_LONG_MINUTES, zurichWallToInstant } from "./time";

/** A moment, as the exact instant (for lengths) and its Zurich calendar date (to spot a changed day). */
export type Moment = { ms: number; date: string };

/** From a server time with offset, e.g. "2026-11-18T19:00:41+01:00". */
export const fromServer = (iso: string): Moment => ({ ms: Date.parse(iso), date: parseServerTime(iso).date });

/** From a form date-time value, e.g. "2026-11-18T19:00" (Zurich wall time, 24-hour). */
export const fromInput = (wall: string): Moment => ({ ms: zurichWallToInstant(wall), date: wall.slice(0, 10) });

export type ChangeSummary = {
  minutes: number;        // length of the shift with the requested times
  deltaMinutes: number;   // compared with the recorded length (+ longer, − shorter)
  overLimit: boolean;     // longer than 14 h → flagged "Over 14 h" for both sides (user's idea)
  inDayChanged: boolean;  // the Clock In moved to another calendar day
  outDayChanged: boolean;
  valid: boolean;         // Clock Out after Clock In
};

/**
 * What a requested change would do to the shift, so neither the employee nor the manager can miss a moved day or a
 * very long result (e.g. 16 Nov typed instead of 17 Nov → a 33-hour shift). Warns, never blocks: the manager decides.
 */
export function summarizeChange(recordedIn: Moment, recordedOut: Moment, recordedMinutes: number, newIn: Moment, newOut: Moment): ChangeSummary {
  // Rounded to the nearest minute, like the server's durations (DECISIONS #21).
  const minutes = Math.round((newOut.ms - newIn.ms) / 60000);
  return {
    minutes,
    deltaMinutes: minutes - recordedMinutes,
    overLimit: minutes > TOO_LONG_MINUTES, // same rule as the server's TooLong flag: strictly more than 14 h
    inDayChanged: newIn.date !== recordedIn.date,
    outDayChanged: newOut.date !== recordedOut.date,
    valid: minutes > 0,
  };
}

/** The summary for a request the server already holds (manager screen): requested times, or the recorded ones if unchanged. */
export function summarizeRequest(r: CorrectionRequest): ChangeSummary {
  const recIn = fromServer(r.shift.clockIn);
  const recOut = fromServer(r.shift.clockOut!);
  return summarizeChange(recIn, recOut, r.shift.durationMinutes ?? 0,
    r.requestedClockIn ? fromServer(r.requestedClockIn) : recIn,
    r.requestedClockOut ? fromServer(r.requestedClockOut) : recOut);
}

export const REQUEST_STATUS_LABELS: Record<RequestStatus, string> = {
  Pending: "Fix pending approval",
  Approved: "Fix approved",
  Rejected: "Fix rejected",
};

/** "2026-11-18T19:00:00.123+01:00" → "2026-11-18T19:00" (Zurich wall time, the format the date-time fields use). */
export const toInput = (iso: string | null) => (iso ? iso.slice(0, 16) : "");

/**
 * The newest request for each shift. A shift can have several over time (e.g. rejected, then asked again);
 * the latest one is what the employee needs to see. The server sends them newest first.
 */
export function latestByShift(requests: CorrectionRequest[]): Map<number, CorrectionRequest> {
  const latest = new Map<number, CorrectionRequest>();
  for (const r of requests) if (!latest.has(r.shiftId)) latest.set(r.shiftId, r);
  return latest;
}

/** Only finished shifts (user's decision), and not while a request for it is already waiting. */
export function canRequestFix(shift: Shift, latest: CorrectionRequest | undefined): boolean {
  return shift.clockOut !== null && latest?.status !== "Pending";
}

/**
 * What to send: only the times the employee actually changed (the server also ignores unchanged ones).
 * `clockIn` / `clockOut` are the form's date-time values ("2026-11-18T17:00").
 */
export function buildRequest(shift: Shift, clockIn: string, clockOut: string, reason: string): NewCorrectionRequest {
  const body: NewCorrectionRequest = { reason: reason.trim() };
  if (clockIn && clockIn !== toInput(shift.clockIn)) body.clockIn = clockIn;
  if (clockOut && clockOut !== toInput(shift.clockOut)) body.clockOut = clockOut;
  return body;
}
