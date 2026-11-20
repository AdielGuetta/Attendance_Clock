import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type AdminShift, type EmployeeSummary, type OnShift } from "../api";
import { addDays, formatDay, formatElapsed, formatHM, parseServerTime, weekDates } from "../time";
import { useZurichClock } from "../useZurichClock";
import { locationLabel } from "../location";
import { mondayOf } from "../reports";
import EditShiftDialog from "./EditShiftDialog";

export const FLAG_LABELS: Record<string, string> = {
  TooLong: "Over 14 h",
  ForgotClockOut: "Forgot to clock out?",
  Corrected: "Corrected",
};

/** Manager-only: who's on shift now, everyone's shifts, and corrections (Plan 10). */
export default function TeamScreen({ onDataChanged }: { onDataChanged?: () => void }) {
  const { now } = useZurichClock();
  const [onShift, setOnShift] = useState<OnShift | null>(null);
  const [employees, setEmployees] = useState<EmployeeSummary[]>([]);
  const [monday, setMonday] = useState<string | null>(null);
  const [employeeId, setEmployeeId] = useState<number | undefined>();
  const [flaggedOnly, setFlaggedOnly] = useState(false);
  const [shifts, setShifts] = useState<AdminShift[] | null>(null);
  const [editing, setEditing] = useState<AdminShift | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadOnShift = useCallback(async () => {
    try {
      const data = await api.admin.onShift();
      setOnShift(data);
      // Start on the current week, as judged by the trusted server time (not this device's clock).
      setMonday((m) => m ?? (data.now ? mondayOf(parseServerTime(data.now).date) : null));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load the team.");
    }
  }, []);

  const loadShifts = useCallback(async () => {
    if (!monday) return;
    try {
      setShifts(await api.admin.shifts(monday, addDays(monday, 6), employeeId, flaggedOnly));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load shifts.");
    }
  }, [monday, employeeId, flaggedOnly]);

  useEffect(() => {
    void loadOnShift();
    api.admin.employees().then(setEmployees).catch(() => {});
  }, [loadOnShift]);
  useEffect(() => { void loadShifts(); }, [loadShifts]);

  // If the time service was down, fall back to the week of the live clock once it ticks.
  useEffect(() => {
    if (!monday && now) setMonday(mondayOf(new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Zurich" }).format(now)));
  }, [monday, now]);

  const afterSave = () => { setEditing(null); void loadOnShift(); void loadShifts(); onDataChanged?.(); /* update the tab badge */ };

  return (
    <section className="team-screen">
      {error && <div className="banner error">{error}</div>}

      <div className="panel">
        <div className="panel-head">
          <h2>On shift now</h2>
          <button className="ghost small-btn" onClick={() => void loadOnShift()}>Refresh</button>
        </div>
        {onShift === null ? <p className="muted">Loading…</p> : onShift.shifts.length === 0 ? (
          <p className="muted">Nobody is on shift right now.</p>
        ) : (
          <ul className="onshift-list">
            {onShift.shifts.map((s) => {
              const start = parseServerTime(s.clockIn);
              const openMin = now ? Math.max(0, Math.floor((now.getTime() - Date.parse(s.clockIn)) / 60000)) : null;
              return (
                <li key={s.id} className={s.flags.includes("ForgotClockOut") ? "warn" : ""}>
                  <span className="who">{s.employeeName}</span>
                  <span className="muted">since {formatDay(start.date)}, {start.hm} {start.label}</span>
                  {openMin !== null && <span>{formatElapsed(openMin)}</span>}
                  <span className="location">{locationLabel(s.workLocation)}</span>
                  {s.flags.map((f) => <span key={f} className="badge">{FLAG_LABELS[f] ?? f}</span>)}
                  <button className="ghost small-btn" onClick={() => setEditing(s)}>Edit</button>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      <div className="panel">
        <div className="panel-head">
          <button className="ghost" onClick={() => monday && setMonday(addDays(monday, -7))} disabled={!monday} aria-label="Previous week">◀</button>
          <h2>{monday ? `${formatDay(monday)} – ${formatDay(addDays(monday, 6))}` : "Shifts"}</h2>
          <button className="ghost" onClick={() => monday && setMonday(addDays(monday, 7))} disabled={!monday} aria-label="Next week">▶</button>
        </div>
        <div className="filters">
          <select value={employeeId ?? ""} onChange={(e) => setEmployeeId(e.target.value ? Number(e.target.value) : undefined)}>
            <option value="">All employees</option>
            {employees.map((e) => <option key={e.id} value={e.id}>{e.displayName}{e.isActive ? "" : " (inactive)"}</option>)}
          </select>
          <label className="check"><input type="checkbox" checked={flaggedOnly} onChange={(e) => setFlaggedOnly(e.target.checked)} /> Flagged only</label>
        </div>

        {shifts === null ? <p className="muted">Loading…</p> : shifts.length === 0 ? <p className="muted">No shifts in this week.</p> : (
          <div className="table-scroll"><table className="shift-table">
            <thead><tr><th>Day</th><th>Employee</th><th>In → Out</th><th>Length</th><th>Where</th><th className="flags-col"></th><th></th></tr></thead>
            <tbody>
              {monday && weekDates(monday).flatMap((date) => shifts.filter((s) => s.zurichDate === date).map((s) => {
                const a = parseServerTime(s.clockIn);
                const b = s.clockOut ? parseServerTime(s.clockOut) : null;
                return (
                  <tr key={s.id}>
                    <td>{formatDay(date)}</td>
                    <td>{s.employeeName}</td>
                    <td className="nums">
                      {a.hm}{s.clockInSource === "ManualCorrection" && <span className="edited" title="Corrected by a manager">✎</span>} →{" "}
                      {b ? <>{b.hm}{b.date !== a.date && <sup> +1</sup>}{s.clockOutSource === "ManualCorrection" && <span className="edited" title="Corrected by a manager">✎</span>}</> : <em>open</em>}
                      <span className="muted small"> {a.label}</span>
                    </td>
                    <td className="nums">{s.durationMinutes !== null ? formatHM(s.durationMinutes) : ""}</td>
                    <td className="location">{locationLabel(s.workLocation)}</td>
                    <td className="flags-col">{s.flags.map((f) => <span key={f} className="badge">{FLAG_LABELS[f] ?? f}</span>)}</td>
                    <td><button className="ghost small-btn" onClick={() => setEditing(s)}>Edit</button></td>
                  </tr>
                );
              }))}
            </tbody>
          </table></div>
        )}
        <p className="fineprint">Times in Zurich time. ✎ = time entered by a manager (a manual correction, with a recorded reason).</p>
      </div>

      {editing && <EditShiftDialog shift={editing} onClose={() => setEditing(null)} onSaved={afterSave} />}
    </section>
  );
}
