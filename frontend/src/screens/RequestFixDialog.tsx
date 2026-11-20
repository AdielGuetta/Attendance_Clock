import { useState, type FormEvent } from "react";
import { api, ApiError, type Shift } from "../api";
import { buildRequest, fromInput, fromServer, summarizeChange, toInput } from "../requests";
import { formatDay, formatDelta, formatHM, formatRecordedTime, parseServerTime } from "../time";

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

  // What the change would do (user's idea): the new length, a moved day, over 14 h. Unchanged fields keep the
  // recorded instant (with its seconds), so the length matches what the server will compute.
  const recIn = fromServer(shift.clockIn);
  const recOut = fromServer(shift.clockOut!);
  const newIn = body.clockIn ? fromInput(body.clockIn) : recIn;
  const newOut = body.clockOut ? fromInput(body.clockOut) : recOut;
  const summary = changed ? summarizeChange(recIn, recOut, shift.durationMinutes ?? 0, newIn, newOut) : null;

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

        {summary && (
          <div className={`change-summary ${summary.valid && summary.overLimit ? "warn" : ""}`} aria-live="polite">
            {!summary.valid ? (
              <strong className="error-text">Clock Out must be after Clock In.</strong>
            ) : (
              <>
                <div>
                  New shift: <span className={summary.inDayChanged ? "changed" : ""}>{formatDay(newIn.date)}</span>{" "}
                  {(body.clockIn ?? toInput(shift.clockIn)).slice(11)} →{" "}
                  <span className={summary.outDayChanged ? "changed" : ""}>{formatDay(newOut.date)}</span>{" "}
                  {(body.clockOut ?? toInput(shift.clockOut)).slice(11)}
                </div>
                <div>
                  Length <strong>{formatHM(summary.minutes)}</strong>{" "}
                  <span className="muted">(was {formatHM(shift.durationMinutes ?? 0)}, {formatDelta(summary.deltaMinutes)})</span>
                </div>
                {(summary.inDayChanged || summary.outDayChanged) && (
                  <div className="small">You changed the <strong>day</strong> of the {summary.inDayChanged && summary.outDayChanged
                    ? "Clock In and Clock Out" : summary.inDayChanged ? "Clock In" : "Clock Out"}. That's fine for a night shift; otherwise check the date.</div>
                )}
                {summary.overLimit && (
                  <div role="alert"><strong>Over 14 hours.</strong> Check the dates. If it's right, send it: your manager will see it flagged.</div>
                )}
              </>
            )}
          </div>
        )}

        <label>
          What happened? (required)
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} required minLength={5} maxLength={300}
            placeholder="e.g. Forgot to clock out, I left at 17:00" />
        </label>

        {error && <div className="banner error" role="alert">{error}</div>}

        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          <button type="submit" className="primary" disabled={busy || !changed || summary?.valid === false}>
            {busy ? "Sending…" : changed ? "Send request" : "Change a time first"}
          </button>
        </div>
      </form>
    </div>
  );
}
