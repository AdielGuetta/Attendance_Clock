import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type CorrectionRequest, type History, type Shift } from "../api";
import { locationLabel } from "../location";
import { canRequestFix, latestByShift, REQUEST_STATUS_LABELS } from "../requests";
import RequestFixDialog from "./RequestFixDialog";
import { addDays, formatDay, formatHM, parseServerTime, weekDates } from "../time";

const FLAG_LABELS: Record<string, string> = {
  TooLong: "Over 14 h",
  ForgotClockOut: "Forgot to clock out",
  Corrected: "Corrected by manager",
};

export default function WeekScreen() {
  const [history, setHistory] = useState<History | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [requests, setRequests] = useState<Map<number, CorrectionRequest>>(new Map()); // Plan 13: newest per shift
  const [fixing, setFixing] = useState<Shift | null>(null);
  const [sent, setSent] = useState(false);

  const loadRequests = useCallback(async () => {
    try { setRequests(latestByShift(await api.myRequests())); } catch { /* the week still shows without statuses */ }
  }, []);

  const load = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    setError(null);
    try {
      // No dates the first time: the server works out "this week" from the official Zurich date.
      const [h] = await Promise.all([api.history(from, to), loadRequests()]);
      setHistory(h);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load your shifts.");
    } finally {
      setLoading(false);
    }
  }, [loadRequests]);

  useEffect(() => { void load(); }, [load]);

  const move = (weeks: number) => {
    if (!history) return;
    const from = addDays(history.from, 7 * weeks);
    void load(from, addDays(from, 6));
  };

  const byDay = new Map<string, Shift[]>();
  history?.shifts.forEach((s) => byDay.set(s.zurichDate, [...(byDay.get(s.zurichDate) ?? []), s]));
  const dayTotal = new Map(history?.days.map((d) => [d.date, d.totalMinutes]) ?? []);

  return (
    <section className="week-screen">
      <div className="week-head">
        <button className="ghost" onClick={() => move(-1)} disabled={!history || loading} aria-label="Previous week">◀</button>
        <h2>{history ? `${formatDay(history.from)} – ${formatDay(history.to)}` : "My week"}</h2>
        <button className="ghost" onClick={() => move(1)} disabled={!history || loading} aria-label="Next week">▶</button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {sent && <div className="banner success" role="status">Request sent. Your manager will approve or reject it.</div>}

      {history && (
        <>
          <div className="week-total">
            Week total <strong>{formatHM(history.totalMinutes)}</strong> h
            {history.openShift && <span className="muted"> · plus a shift in progress</span>}
          </div>

          <ul className="days">
            {weekDates(history.from).map((date) => {
              const shifts = byDay.get(date) ?? [];
              const inProgress = history.openShift?.zurichDate === date ? history.openShift : null;
              return (
                <li key={date} className={shifts.length || inProgress ? "day" : "day empty"}>
                  <div className="day-name">{formatDay(date)}</div>
                  <div className="day-shifts">
                    {shifts.map((s) => (
                      <ShiftRow key={s.id} shift={s} request={requests.get(s.id)}
                        onRequestFix={() => { setSent(false); setFixing(s); }} />
                    ))}
                    {inProgress && <ShiftRow shift={inProgress} />}
                    {!shifts.length && !inProgress && <span className="muted">-</span>}
                  </div>
                  <div className="day-total">{shifts.length ? formatHM(dayTotal.get(date) ?? 0) : ""}</div>
                </li>
              );
            })}
          </ul>
          <p className="fineprint">Times in Zurich time. A shift counts on the day it started. Each shift is rounded to the nearest minute.
            {" "}Wrong time on a finished shift? Use "Request a fix" and your manager will review it.</p>
        </>
      )}

      {fixing && (
        <RequestFixDialog shift={fixing} onClose={() => setFixing(null)}
          onSent={() => { setFixing(null); setSent(true); void loadRequests(); }} />
      )}
    </section>
  );
}

function ShiftRow({ shift, request, onRequestFix }: { shift: Shift; request?: CorrectionRequest; onRequestFix?: () => void }) {
  const start = parseServerTime(shift.clockIn);
  const end = shift.clockOut ? parseServerTime(shift.clockOut) : null;
  return (
    <div className="shift">
      <span className="shift-times">
        {start.hm} → {end ? <>{end.hm}{end.date !== start.date && <sup> +1</sup>}</> : <em>in progress</em>}
        <span className="muted small"> {start.label}</span>
      </span>
      {shift.durationMinutes !== null && <span className="shift-len">{formatHM(shift.durationMinutes)}</span>}
      <span className="location muted">{locationLabel(shift.workLocation)}</span>
      {shift.flags.map((f) => <span key={f} className="badge">{FLAG_LABELS[f] ?? f}</span>)}
      {request && (
        <span className={`badge request-${request.status.toLowerCase()}`}
          title={request.decidedBy ? `by ${request.decidedBy}` : `"${request.reason}"`}>
          {REQUEST_STATUS_LABELS[request.status]}
        </span>
      )}
      {onRequestFix && canRequestFix(shift, request) && (
        <button className="ghost small-btn" onClick={onRequestFix}>Request a fix</button>
      )}
    </div>
  );
}
