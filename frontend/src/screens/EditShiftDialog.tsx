import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError, type AdminShift, type ShiftAudit } from "../api";
import { locationLabel } from "../location";
import { formatDay, formatRecordedTime, parseServerTime } from "../time";

/** "2026-11-20T17:00:00+01:00" → "2026-11-20T17:00" (Zurich wall time, the format of a datetime-local input). */
const toInput = (iso: string | null) => (iso ? iso.slice(0, 16) : "");

/**
 * Manager correction of one shift (Plan 10). Only changed fields are sent; the reason is mandatory;
 * the version stamp makes the server refuse if the shift changed meanwhile. Below: the audit history.
 */
export default function EditShiftDialog({ shift, onClose, onSaved }: { shift: AdminShift; onClose: () => void; onSaved: () => void }) {
  const [clockIn, setClockIn] = useState(toInput(shift.clockIn));
  const [clockOut, setClockOut] = useState(toInput(shift.clockOut));
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ambiguous, setAmbiguous] = useState(false); // DST "fall back": the typed time happens twice
  const [audit, setAudit] = useState<ShiftAudit[] | null>(null);

  useEffect(() => { api.admin.audit(shift.id).then(setAudit).catch(() => setAudit([])); }, [shift.id]);

  const save = async (offset?: "+02:00" | "+01:00") => {
    setBusy(true);
    setError(null);
    try {
      const changedIn = clockIn !== toInput(shift.clockIn) ? clockIn : undefined;
      const changedOut = clockOut && clockOut !== toInput(shift.clockOut) ? clockOut : undefined;
      // When the server says "this time happens twice", the manager picks which one and we resend with that offset.
      const withOffset = (t?: string) => (t && offset ? `${t}${offset}` : t);
      await api.admin.correct(shift.id, { clockIn: withOffset(changedIn), clockOut: withOffset(changedOut), reason, rowVersion: shift.rowVersion });
      onSaved();
    } catch (err) {
      if (err instanceof ApiError && err.code === "AMBIGUOUS_TIME") { setAmbiguous(true); setError(err.detail ?? err.title); }
      else if (err instanceof ApiError) { setAmbiguous(false); setError(`${err.title}${err.detail ? " " + err.detail : ""}`); }
      else setError("Something went wrong. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  const submit = (e: FormEvent) => { e.preventDefault(); void save(); };
  const start = parseServerTime(shift.clockIn);

  return (
    <div className="dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="edit-title" onClick={onClose}>
      <form className="card dialog" onSubmit={submit} onClick={(e) => e.stopPropagation()}>
        <h2 id="edit-title">Correct shift</h2>
        <p className="muted">{shift.employeeName} · {formatDay(start.date)} · {locationLabel(shift.workLocation)} · times in Zurich time</p>

        <div className="two-col">
          <label>Clock In <input type="datetime-local" value={clockIn} onChange={(e) => setClockIn(e.target.value)} required /></label>
          <label>Clock Out <input type="datetime-local" value={clockOut} onChange={(e) => setClockOut(e.target.value)} placeholder="still open" /></label>
        </div>
        {!shift.clockOut && <p className="muted small">This shift is still open. Set a Clock Out to close it.</p>}

        <label>
          Reason (required)
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} required minLength={5}
            placeholder="e.g. Forgot to clock out, confirmed left at 17:00" />
        </label>

        {error && <div className="banner error" role="alert">{error}</div>}
        {ambiguous && (
          <div className="ambiguous">
            <button type="button" className="ghost" disabled={busy} onClick={() => void save("+02:00")}>The first one (summer time, CEST)</button>
            <button type="button" className="ghost" disabled={busy} onClick={() => void save("+01:00")}>The second one (winter time, CET)</button>
          </div>
        )}

        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="primary" disabled={busy}>{busy ? "Saving…" : "Save correction"}</button>
        </div>

        <h3>History</h3>
        {audit === null ? <p className="muted small">Loading…</p> : audit.length === 0 ? (
          <p className="muted small">No corrections yet. Times so far came from the time service ({shift.clockInSource}).</p>
        ) : (
          <ul className="audit">
            {audit.map((a) => (
              <li key={a.id}>
                <strong>{a.field === "ClockIn" ? "Clock In" : "Clock Out"}</strong>{" "}
                {a.oldValue === "(open)" ? "(open)" : formatRecordedTime(a.oldValue)} → {formatRecordedTime(a.newValue)}
                <div className="muted small">by {a.changedBy}, {formatDay(parseServerTime(a.changedAt).date)} {formatRecordedTime(a.changedAt)} · "{a.reason}"</div>
              </li>
            ))}
          </ul>
        )}
      </form>
    </div>
  );
}
