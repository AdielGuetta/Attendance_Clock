import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type PunchStatus } from "../api";
import { formatDay, formatElapsed, formatRecordedTime, formatZurichNow, isLikelyForgotten, openMinutes, parseServerTime } from "../time";
import { useZurichClock } from "../useZurichClock";
import { loadLastLocation, locationLabel, saveLastLocation, WORK_LOCATIONS, type WorkLocation } from "../location";
import { getSession } from "../session";
import RecentShifts from "./RecentShifts";

type Banner = { kind: "success" | "error" | "info"; text: string } | null;

export default function ClockScreen({ onViewAll }: { onViewAll: () => void }) {
  const { now, error: clockError } = useZurichClock();
  const [status, setStatus] = useState<PunchStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [banner, setBanner] = useState<Banner>(null);
  const [recentKey, setRecentKey] = useState(0); // bumped after every punch → the Recent shifts box reloads
  // Plan 12: where I'm working. Pre-selected from last time; the very first time nothing is selected, so it's a real choice.
  const employeeId = getSession()?.employee.id ?? 0;
  const [location, setLocation] = useState<WorkLocation | null>(() => loadLastLocation(employeeId));

  const refresh = useCallback(async () => {
    try { setStatus(await api.status()); } catch { /* 401 is handled globally; keep the last known status */ }
  }, []);

  // Load the status now, and again whenever the user comes back to this tab: another device may have punched.
  useEffect(() => {
    void refresh();
    const onFocus = () => void refresh();
    window.addEventListener("focus", onFocus);
    return () => window.removeEventListener("focus", onFocus);
  }, [refresh]);

  const onShift = status?.state === "OnShift";

  const punch = async () => {
    setBusy(true); // disables the button: no double-clicks (EC-1). The server guards anyway.
    setBanner(null);
    try {
      if (onShift) {
        const shift = await api.clockOut(status!.openShift!.id); // the shift THIS screen shows (Plan 14)
        setBanner({ kind: "success", text: `Clocked out at ${formatRecordedTime(shift.clockOut!)} · source ${shift.clockOutSource}` });
      } else {
        if (!location) return; // the button is disabled until a location is picked; this is just a safety net
        const shift = await api.clockIn(location);
        saveLastLocation(employeeId, location);
        setBanner({ kind: "success", text: `Clocked in at ${formatRecordedTime(shift.clockIn)} · ${locationLabel(shift.workLocation)} · source ${shift.clockInSource}` });
      }
    } catch (err) {
      setBanner({ kind: "error", text: messageFor(err) });
    } finally {
      await refresh(); // show the server's truth, whatever happened
      setRecentKey((k) => k + 1);
      setBusy(false);
    }
  };

  const zurich = now ? formatZurichNow(now) : null;
  const open = status?.openShift;
  const elapsedMinutes = open && now ? openMinutes(open.clockIn, now) : null;
  const openIn = open ? parseServerTime(open.clockIn) : null;
  // Plan 11: an open shift older than 14 h was probably forgotten. We alert, we never auto-close (no invented times).
  const forgotten = open && now ? isLikelyForgotten(open.clockIn, now) : false;

  return (
    <section className="clock-screen">
      <div className="live-clock" aria-live="off">
        <div className="live-time">{zurich ? zurich.time : "--:--:--"}</div>
        <div className="live-date">
          {zurich ? <>{zurich.date} · Zurich time ({zurich.label})</> : clockError ? "Official time unavailable right now" : "Getting the official time…"}
        </div>
      </div>

      <div className={`status ${onShift ? "on" : "off"}`}>
        {status === null ? "Loading your status…" : onShift && openIn ? (
          <>
            <strong>On shift</strong> since {openIn.date === zurich?.dateIso ? "" : `${formatDay(openIn.date)}, `}{openIn.hm} {openIn.label}
            {elapsedMinutes !== null && <> · {formatElapsed(elapsedMinutes)}</>}
            {open?.workLocation && <> · {locationLabel(open.workLocation)}</>}
          </>
        ) : (
          <strong>Off shift</strong>
        )}
      </div>

      {forgotten && openIn && elapsedMinutes !== null && (
        <div className="banner warn" role="alert">
          <strong>Did you forget to clock out?</strong> You've been on shift since {formatDay(openIn.date)}, {openIn.hm} {openIn.label}
          {" "}({formatElapsed(elapsedMinutes)}). If you're still working, ignore this. If you left earlier, clock out now
          and ask your manager to correct the end time. Your manager has been flagged too.
        </div>
      )}

      {status !== null && !onShift && (
        <fieldset className="location-pick">
          <legend>Where are you working?</legend>
          {WORK_LOCATIONS.map((l) => (
            <button key={l} type="button" className={l === location ? "selected" : ""} aria-pressed={l === location}
              onClick={() => setLocation(l)} disabled={busy}>
              {locationLabel(l)}
            </button>
          ))}
        </fieldset>
      )}

      <button
        className={`punch ${onShift ? "out" : "in"}`}
        onClick={punch}
        disabled={busy || status === null || (!onShift && !location)}
      >
        {busy ? "Recording…" : onShift ? "Clock Out" : location ? "Clock In" : "Pick a location"}
      </button>

      {banner && <div className={`banner ${banner.kind}`} role="status">{banner.text}</div>}

      <RecentShifts todayIso={zurich?.dateIso ?? null} refreshKey={recentKey} onViewAll={onViewAll} />

      <p className="fineprint">
        The recorded time always comes from an official time service, never from this device's clock.
        The clock above is for display only.
      </p>
    </section>
  );
}

function messageFor(err: unknown): string {
  if (!(err instanceof ApiError)) return "Something went wrong. Please try again.";
  switch (err.code) {
    // A stale tab or another device (EC-3/EC-4). The status is refreshed right after, so say so.
    case "ALREADY_ON_SHIFT":
      return `${err.detail ?? err.title} Your status below is up to date.`;
    case "NOT_ON_SHIFT":
      return "You're not on shift: this shift was already closed (maybe from another device). Your status below is up to date.";
    // Plan 14: this tab still showed a shift that was closed elsewhere, and a newer one is open. Nothing was changed.
    case "SHIFT_CHANGED":
      return `${err.detail ?? err.title} Your status below is up to date; press Clock Out again if you want to end the current shift.`;
    case "INVALID_LOCATION":
      return "Choose Office or Remote, then clock in.";
    case "TIME_SOURCE_UNAVAILABLE":
    case "TIME_SOURCE_INCONSISTENT":
      return "Couldn't get the official time. Nothing was recorded. Please try again in a moment."; // fail closed (EC-8)
    default:
      return err.message;
  }
}
