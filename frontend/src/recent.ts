// Logic for the "Recent shifts" box on the Clock screen, kept separate from the screen so it's easy to test.
import type { History, Shift } from "./api";
import { addDays } from "./time";

/** How far back the Clock screen looks for recent shifts. */
export const RECENT_DAYS = 14;

/** The date range to ask the server for: the last 14 Zurich days, ending today. */
export function recentRange(todayIso: string): { from: string; to: string } {
  return { from: addDays(todayIso, -(RECENT_DAYS - 1)), to: todayIso };
}

/**
 * The newest shifts first, at most `limit`. A shift still in progress comes first: it's the most recent thing
 * the employee did. The server sends history oldest-first, so we flip it.
 */
export function pickRecent(history: History, limit = 5): Shift[] {
  const closed = [...history.shifts].sort((a, b) => Date.parse(b.clockIn) - Date.parse(a.clockIn));
  const all = history.openShift ? [history.openShift, ...closed] : closed;
  return all.slice(0, limit);
}
