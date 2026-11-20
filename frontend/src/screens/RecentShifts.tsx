import { useEffect, useState } from "react";
import { api, type Shift } from "../api";
import { pickRecent, recentRange, RECENT_DAYS } from "../recent";
import { formatDay, formatHM, parseServerTime } from "../time";

const FLAG_LABELS: Record<string, string> = { TooLong: "Over 14 h", Corrected: "Corrected by manager" };

/**
 * "Recent shifts" on the Clock screen (proj2 warm-up).
 * Uses the EXISTING history endpoint (GET /api/shifts/mine?from=&to=), so no backend change was needed.
 * `refreshKey` changes after every punch, which makes this box load again.
 */
export default function RecentShifts({ todayIso, refreshKey, onViewAll }: {
  todayIso: string | null;   // Zurich date from the server-seeded live clock
  refreshKey: number;
  onViewAll: () => void;
}) {
  const [shifts, setShifts] = useState<Shift[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!todayIso) return;                       // wait until we know "today" (from the official time)
    const { from, to } = recentRange(todayIso);
    let cancelled = false;
    api.history(from, to)
      .then((h) => { if (!cancelled) { setShifts(pickRecent(h)); setFailed(false); } })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };          // ignore an old answer if a newer request started
  }, [todayIso, refreshKey]);

  return (
    <div className="panel recent">
      <div className="panel-head">
        <h2>Recent shifts</h2>
        <button className="ghost small-btn" onClick={onViewAll}>View all ›</button>
      </div>
      {failed ? <p className="muted">Couldn't load your recent shifts.</p>
        : shifts === null ? <p className="muted">Loading…</p>
        : shifts.length === 0 ? <p className="muted">No shifts in the last {RECENT_DAYS} days yet.</p>
        : (
          <table className="shift-table">
            <thead><tr><th>Day</th><th>In → Out</th><th>Length</th><th></th></tr></thead>
            <tbody>
              {shifts.map((s) => {
                const a = parseServerTime(s.clockIn);
                const b = s.clockOut ? parseServerTime(s.clockOut) : null;
                return (
                  <tr key={s.id}>
                    <td>{formatDay(a.date)}</td>
                    <td className="nums">
                      {a.hm} → {b ? <>{b.hm}{b.date !== a.date && <sup> +1</sup>}</> : <em>in progress</em>}
                      <span className="muted small"> {a.label}</span>
                    </td>
                    <td className="nums">{s.durationMinutes !== null ? formatHM(s.durationMinutes) : ""}</td>
                    <td>{s.flags.filter((f) => FLAG_LABELS[f]).map((f) => <span key={f} className="badge">{FLAG_LABELS[f]}</span>)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
    </div>
  );
}
