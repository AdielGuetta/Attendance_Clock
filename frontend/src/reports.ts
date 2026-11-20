// Logic for the manager Reports tab (Plan 15), kept apart from the screen so it's easy to test.
// Dates are "YYYY-MM-DD" Zurich calendar dates; "today" comes from the server-seeded live clock, not the device.
import { addDays } from "./time";

export type Period = { from: string; to: string };
export type PresetId = "thisWeek" | "thisMonth" | "lastMonth";

export const PRESETS: { id: PresetId; label: string }[] = [
  { id: "thisWeek", label: "This week" },
  { id: "thisMonth", label: "This month" },
  { id: "lastMonth", label: "Last month" },
];

/** Monday of the week containing a date (weeks run Monday–Sunday, like My week). */
export function mondayOf(date: string): string {
  const dow = new Date(`${date}T00:00:00Z`).getUTCDay(); // 0 = Sunday
  return addDays(date, -((dow + 6) % 7));
}

/** First and last day of the month containing `date`. */
function monthOf(date: string): Period {
  const from = `${date.slice(0, 7)}-01`;
  const [y, m] = from.split("-").map(Number);
  const firstOfNext = m === 12 ? `${y + 1}-01-01` : `${y}-${String(m + 1).padStart(2, "0")}-01`;
  return { from, to: addDays(firstOfNext, -1) };
}

export function presetPeriod(id: PresetId, today: string): Period {
  switch (id) {
    case "thisWeek": { const monday = mondayOf(today); return { from: monday, to: addDays(monday, 6) }; }
    case "thisMonth": return monthOf(today);
    case "lastMonth": return monthOf(addDays(monthOf(today).from, -1)); // the day before the 1st is in last month
  }
}

/** Every date from `from` to `to`, so days without shifts show up too (a gap is information). */
export function datesBetween(from: string, to: string): string[] {
  const dates: string[] = [];
  for (let d = from; d <= to && dates.length < 400; d = addDays(d, 1)) dates.push(d);
  return dates;
}

/** Same name the server gives the file (ReportsController.FileName). */
export const reportFileName = (p: Period) => `attendance-${p.from}_${p.to}.csv`;

/** Width of a day's bar, as a percentage of the busiest day. */
export const barPercent = (minutes: number, maxMinutes: number) =>
  maxMinutes > 0 ? Math.round((minutes / maxMinutes) * 100) : 0;
