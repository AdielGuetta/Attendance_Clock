import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type CorrectionRequest } from "../api";
import { locationLabel } from "../location";
import { REQUEST_STATUS_LABELS } from "../requests";
import { formatDay, formatHM, parseServerTime } from "../time";

/** "08:00" or "08:00 +1" when the time is on the day after the shift started. */
function hm(iso: string, startDate: string) {
  const t = parseServerTime(iso);
  return <>{t.hm}{t.date !== startDate && <sup> +1</sup>}</>;
}

/**
 * Manager-only: employees' correction requests (Plan 13). Approve = an audited manager correction (Plan 10);
 * reject = one tap, no explanation (user's decision).
 */
export default function RequestsScreen({ onDataChanged }: { onDataChanged?: () => void }) {
  const [pending, setPending] = useState<CorrectionRequest[] | null>(null);
  const [decided, setDecided] = useState<CorrectionRequest[]>([]);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [errors, setErrors] = useState<Record<number, string>>({});
  const [loadError, setLoadError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const all = await api.admin.requests();
      setPending(all.filter((r) => r.status === "Pending").reverse()); // oldest first: first come, first served
      setDecided(all.filter((r) => r.status !== "Pending").slice(0, 10));
      setLoadError(null);
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.message : "Couldn't load the requests.");
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const decide = async (r: CorrectionRequest, action: "approve" | "reject") => {
    setBusyId(r.id);
    setErrors((e) => { const next = { ...e }; delete next[r.id]; return next; });
    try {
      await (action === "approve" ? api.admin.approve(r.id) : api.admin.reject(r.id));
    } catch (err) {
      // e.g. OVERLAPS_ANOTHER_SHIFT: the request stays pending, and the manager sees why.
      const text = err instanceof ApiError ? `${err.title}${err.detail ? " " + err.detail : ""}` : "Something went wrong.";
      setErrors((e) => ({ ...e, [r.id]: text }));
    } finally {
      setBusyId(null);
      await load();
      onDataChanged?.(); // update the tab badge
    }
  };

  return (
    <section className="team-screen">
      {loadError && <div className="banner error">{loadError}</div>}

      <div className="panel">
        <div className="panel-head">
          <h2>Waiting for you</h2>
          <button className="ghost small-btn" onClick={() => void load()}>Refresh</button>
        </div>
        {pending === null ? <p className="muted">Loading…</p> : pending.length === 0 ? (
          <p className="muted">No requests waiting.</p>
        ) : (
          <ul className="request-list">
            {pending.map((r) => {
              const start = parseServerTime(r.shift.clockIn).date;
              const newIn = r.requestedClockIn ?? r.shift.clockIn;
              const newOut = r.requestedClockOut ?? r.shift.clockOut!;
              const newMinutes = Math.round((Date.parse(newOut) - Date.parse(newIn)) / 60000);
              return (
                <li key={r.id}>
                  <div className="request-head">
                    <strong>{r.employeeName}</strong>
                    <span className="muted">{formatDay(start)} · {locationLabel(r.shift.workLocation)} · request #{r.id}</span>
                  </div>
                  <table className="compare">
                    <tbody>
                      <tr>
                        <th>Recorded</th>
                        <td className="nums">{hm(r.shift.clockIn, start)} → {hm(r.shift.clockOut!, start)}</td>
                        <td className="nums muted">{r.shift.durationMinutes !== null ? formatHM(r.shift.durationMinutes) : ""}</td>
                      </tr>
                      <tr>
                        <th>Requested</th>
                        <td className="nums">
                          <span className={r.requestedClockIn ? "changed" : ""}>{hm(newIn, start)}</span> →{" "}
                          <span className={r.requestedClockOut ? "changed" : ""}>{hm(newOut, start)}</span>
                        </td>
                        <td className="nums">{formatHM(newMinutes)}</td>
                      </tr>
                    </tbody>
                  </table>
                  <p className="request-reason">"{r.reason}"</p>
                  {errors[r.id] && <div className="banner error" role="alert">{errors[r.id]}</div>}
                  <div className="request-actions">
                    <button className="ghost" disabled={busyId !== null} onClick={() => void decide(r, "reject")}>Reject</button>
                    <button className="primary" disabled={busyId !== null} onClick={() => void decide(r, "approve")}>
                      {busyId === r.id ? "Saving…" : "Approve"}
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
        <p className="fineprint">Approving applies the requested times as a manager correction: labelled ✎, with an entry in the shift's history.</p>
      </div>

      {decided.length > 0 && (
        <div className="panel">
          <div className="panel-head"><h2>Recently decided</h2></div>
          <div className="table-scroll"><table className="shift-table">
            <thead><tr><th>Employee</th><th>Shift</th><th>Status</th><th>By</th></tr></thead>
            <tbody>
              {decided.map((r) => (
                <tr key={r.id}>
                  <td>{r.employeeName}</td>
                  <td>{formatDay(parseServerTime(r.shift.clockIn).date)}</td>
                  <td><span className={`badge request-${r.status.toLowerCase()}`}>{REQUEST_STATUS_LABELS[r.status]}</span></td>
                  <td className="muted">{r.decidedBy}</td>
                </tr>
              ))}
            </tbody>
          </table></div>
        </div>
      )}
    </section>
  );
}
