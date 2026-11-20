// Time display helpers. Everything is shown in Zurich time, whatever time zone the device is set to.
// Recorded times come from the server as ISO strings that already carry the Zurich offset,
// e.g. "2026-11-20T08:02:11.916+01:00", so we read the wall-clock parts straight from the string.

const ZURICH = "Europe/Zurich";

/** "+01:00" → "CET", "+02:00" → "CEST" (Zurich's only two offsets). */
export function zoneLabel(offset: string): string {
  if (offset === "+01:00") return "CET";
  if (offset === "+02:00") return "CEST";
  return `UTC${offset}`;
}

const isoParts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?([+-]\d{2}:\d{2}|Z)$/;

/** Splits a server time like "2026-11-20T08:02:11.9+01:00" into its Zurich wall-clock parts. */
export function parseServerTime(iso: string) {
  const m = isoParts.exec(iso);
  if (!m) throw new Error(`Unexpected time format: ${iso}`);
  const [, y, mo, d, h, mi, s, off] = m;
  return { date: `${y}-${mo}-${d}`, hm: `${h}:${mi}`, hms: `${h}:${mi}:${s}`, label: zoneLabel(off === "Z" ? "+00:00" : off) };
}

/** "08:02:11 CET": a recorded time with its zone label. */
export function formatRecordedTime(iso: string): string {
  const p = parseServerTime(iso);
  return `${p.hms} ${p.label}`;
}

/** 510 → "8:30" (hours:minutes), for tables and totals. */
export function formatHM(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${h}:${String(m).padStart(2, "0")}`;
}

/** 192 → "3 h 12 min"; 45 → "45 min". For "on shift for …". */
export function formatElapsed(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return h > 0 ? `${h} h ${String(m).padStart(2, "0")} min` : `${m} min`;
}

/** The live clock: a Date shown as Zurich time, e.g. { time: "14:30:05", date: "Wed 15 Jul 2026", label: "CEST" }. */
export function formatZurichNow(now: Date) {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone: ZURICH, hourCycle: "h23", hour: "2-digit", minute: "2-digit", second: "2-digit",
    weekday: "short", day: "numeric", month: "short", year: "numeric", timeZoneName: "shortOffset",
  }).formatToParts(now);
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? "";
  // shortOffset gives "GMT+1" / "GMT+2", which we turn into CET / CEST.
  const offset = get("timeZoneName") === "GMT+2" ? "+02:00" : "+01:00";
  return {
    time: `${get("hour")}:${get("minute")}:${get("second")}`,
    date: `${get("weekday")} ${get("day")} ${get("month")} ${get("year")}`,
    // "2026-11-20": the Zurich calendar date, to compare with a shift's date
    dateIso: new Intl.DateTimeFormat("en-CA", { timeZone: ZURICH, year: "numeric", month: "2-digit", day: "2-digit" }).format(now),
    label: zoneLabel(offset),
  };
}

/** Same threshold as the server (ShiftService.TooLongThreshold): an open shift older than this was probably forgotten. */
export const FORGOT_CLOCK_OUT_MINUTES = 14 * 60;

/** Minutes a shift has been open, by the given "now" (the cosmetic live clock). */
export function openMinutes(clockInIso: string, now: Date): number {
  return Math.max(0, Math.floor((now.getTime() - Date.parse(clockInIso)) / 60000));
}

/** Same 14 h threshold, for finished shifts: longer than this is flagged "Over 14 h" (F7). */
export const TOO_LONG_MINUTES = FORGOT_CLOCK_OUT_MINUTES;

/** Zurich's offset from UTC at a given instant, in minutes (60 in winter, 120 in summer). */
function zurichOffsetMinutes(utcMs: number): number {
  const name = new Intl.DateTimeFormat("en-GB", { timeZone: ZURICH, timeZoneName: "shortOffset" })
    .formatToParts(new Date(utcMs)).find((p) => p.type === "timeZoneName")?.value;
  return name === "GMT+2" ? 120 : 60;
}

/**
 * A typed Zurich wall time ("2026-11-18T22:30", the format of a datetime-local input) → the real instant (ms).
 * Used only for DISPLAY (lengths in the fix-request form, Plan 13); the server does the official reading.
 * Two passes, so the summer/winter offset of that very moment is used (e.g. the night the clocks change).
 */
export function zurichWallToInstant(wall: string): number {
  const asIfUtc = Date.parse(`${wall}:00Z`);
  const firstGuess = asIfUtc - zurichOffsetMinutes(asIfUtc) * 60000;
  return asIfUtc - zurichOffsetMinutes(firstGuess) * 60000;
}

/** 30 → "+0:30", -90 → "−1:30", 0 → "±0:00". */
export function formatDelta(minutes: number): string {
  const sign = minutes > 0 ? "+" : minutes < 0 ? "−" : "±";
  return `${sign}${formatHM(Math.abs(minutes))}`;
}

/** True when an open shift is older than 14 hours (F7, Plan 11). */
export function isLikelyForgotten(clockInIso: string, now: Date): boolean {
  return openMinutes(clockInIso, now) > FORGOT_CLOCK_OUT_MINUTES;
}

// ── Calendar dates ("2026-11-16"), done in UTC so the device's time zone can't shift them ──

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** The 7 dates of the week starting at `monday`. */
export function weekDates(monday: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

/** "2026-11-16" → "Mon 16 Nov". */
export function formatDay(date: string): string {
  return new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", weekday: "short", day: "numeric", month: "short" })
    .format(new Date(`${date}T00:00:00Z`));
}
