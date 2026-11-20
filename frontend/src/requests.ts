// Logic for correction requests (Plan 13), kept apart from the screens so it's easy to test.
import type { CorrectionRequest, NewCorrectionRequest, RequestStatus, Shift } from "./api";

export const REQUEST_STATUS_LABELS: Record<RequestStatus, string> = {
  Pending: "Fix pending approval",
  Approved: "Fix approved",
  Rejected: "Fix rejected",
};

/** "2026-11-18T19:00:00.123+01:00" → "2026-11-18T19:00" (Zurich wall time, the format of a datetime-local input). */
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
 * `clockIn` / `clockOut` are the form's datetime-local values.
 */
export function buildRequest(shift: Shift, clockIn: string, clockOut: string, reason: string): NewCorrectionRequest {
  const body: NewCorrectionRequest = { reason: reason.trim() };
  if (clockIn && clockIn !== toInput(shift.clockIn)) body.clockIn = clockIn;
  if (clockOut && clockOut !== toInput(shift.clockOut)) body.clockOut = clockOut;
  return body;
}
