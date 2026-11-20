// 24-hour time entry (proj2). The browser's built-in date-and-time box follows the computer's region settings and can
// show "05:00 PM", while the rest of the app shows "17:00". So the time is a plain 24-hour field (type it, or pick it
// from the hour/minute roller), and only the DATE uses the browser's calendar picker.

/**
 * Reads what was typed as a 24-hour time and returns it as "HH:mm", or null if it isn't one.
 * Forgiving about the shape ("7:05", "0705", "705" → "07:05"; "1700" → "17:00"), strict about the values
 * (hours 00–23, minutes 00–59): "25:00", "17:60" and "5pm" are refused.
 */
export function normalizeTime(text: string): string | null {
  const m = /^(\d{1,2}):?(\d{2})$/.exec(text.trim());
  if (!m) return null;
  const h = Number(m[1]);
  const min = Number(m[2]);
  if (h > 23 || min > 59) return null;
  return `${String(h).padStart(2, "0")}:${String(min).padStart(2, "0")}`;
}

/**
 * Date ("2026-11-18") + typed time → "2026-11-18T17:00" (the format the screens already use and the server reads).
 * Both empty → "" (no value, e.g. a shift still open). Only one, or a bad time → null (not ready: Save stays off).
 */
export function joinDateTime(date: string, time: string): string | null {
  if (!date && !time.trim()) return "";
  const t = normalizeTime(time);
  return date && t ? `${date}T${t}` : null;
}

/** "00", "01", … for the roller columns. */
export const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, "0"));
export const MINUTES = Array.from({ length: 60 }, (_, i) => String(i).padStart(2, "0"));
