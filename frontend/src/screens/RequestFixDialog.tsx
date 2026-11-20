import { useState, type FormEvent } from "react";
import { api, ApiError, type Shift } from "../api";
import { buildRequest, toInput } from "../requests";
import { formatDay, formatRecordedTime, parseServerTime } from "../time";

/**
 * An employee asks the manager to fix the times of one FINISHED shift (Plan 13).
 * The typed times are only a suggestion: nothing changes until a manager approves.
 */
export default function RequestFixDialog({ shift, onClose, onSent }: { shift: Shift; onClose: () => void; onSent: () => void }) {
  const [clockIn, setClockIn] = useState(toInput(shift.clockIn));
  const [clockOut, setClockOut] = useState(toInput(shift.clockOut));
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const body = buildRequest(shift, clockIn, clockOut, reason);
  const changed = body.clockIn !== undefined || body.clockOut !== undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.requestCorrection(shift.id, body);
      onSent();
    } catch (err) {
      setError(err instanceof ApiError ? `${err.title}${err.detail ? " " + err.detail : ""}` : "Something went wrong. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  const start = parseServerTime(shift.clockIn);

  return (
    <div className="dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="fix-title" onClick={onClose}>
      <form className="card dialog" onSubmit={submit} onClick={(e) => e.stopPropagation()}>
        <h2 id="fix-title">Request a fix</h2>
        <p className="muted">
          {formatDay(start.date)} · recorded {formatRecordedTime(shift.clockIn)} → {formatRecordedTime(shift.clockOut!)}
        </p>
        <p className="muted small">Change the time that's wrong. Your manager will approve or reject it; until then nothing changes.</p>

        <div className="two-col">
          <label>Clock In <input type="datetime-local" value={clockIn} onChange={(e) => setClockIn(e.target.value)} required /></label>
          <label>Clock Out <input type="datetime-local" value={clockOut} onChange={(e) => setClockOut(e.target.value)} required /></label>
        </div>

        <label>
          What happened? (required)
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} required minLength={5} maxLength={300}
            placeholder="e.g. Forgot to clock out, I left at 17:00" />
        </label>

        {error && <div className="banner error" role="alert">{error}</div>}

        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !changed}>
            {busy ? "Sending…" : changed ? "Send request" : "Change a time first"}
          </button>
        </div>
      </form>
    </div>
  );
}
