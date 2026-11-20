import { useCallback, useEffect, useState } from "react";
import { api, setUnauthorizedHandler } from "./api";
import { clearSession, getSession, type Session } from "./session";
import LoginScreen from "./screens/LoginScreen";
import ClockScreen from "./screens/ClockScreen";
import WeekScreen from "./screens/WeekScreen";
import TeamScreen from "./screens/TeamScreen";
import RequestsScreen from "./screens/RequestsScreen";
import ReportsScreen from "./screens/ReportsScreen";

type Tab = "clock" | "week" | "team" | "requests" | "reports";

const FORGOTTEN_POLL_MS = 2 * 60 * 1000;

export default function App() {
  const [session, setSession] = useState<Session | null>(() => getSession());
  const [tab, setTab] = useState<Tab>("clock");
  const [notice, setNotice] = useState<string | null>(null);
  const [forgottenCount, setForgottenCount] = useState(0);
  const [pendingCount, setPendingCount] = useState(0); // Plan 13: correction requests waiting for a manager

  // The tab is only a convenience: the server refuses manager endpoints to non-managers anyway (403).
  const isManager = session?.employee.role === "Manager";

  // If the server rejects the badge (expired after 60 min, or account deactivated), go back to login (EC-14).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      setSession(null);
      setNotice("Your session has ended. Please log in again.");
    });
  }, []);

  // Plan 11: managers see a red count on the Team tab when someone's shift has been open > 14 h.
  // The flag comes from the server, computed against the trusted clock.
  const refreshForgotten = useCallback(async () => {
    if (!isManager) return;
    try {
      const onShift = await api.admin.onShift();
      setForgottenCount(onShift.shifts.filter((s) => s.flags.includes("ForgotClockOut")).length);
    } catch { /* keep the last count; 401 is handled globally */ }
  }, [isManager]);

  // Plan 13: count on the Requests tab, refreshed on the same schedule as the forgotten-shift badge.
  const refreshPending = useCallback(async () => {
    if (!isManager) return;
    try { setPendingCount((await api.admin.requests("Pending")).length); } catch { /* keep the last count */ }
  }, [isManager]);

  useEffect(() => {
    if (!isManager) { setForgottenCount(0); setPendingCount(0); return; }
    const refreshBadges = () => { void refreshForgotten(); void refreshPending(); };
    refreshBadges();
    const id = setInterval(refreshBadges, FORGOTTEN_POLL_MS);
    window.addEventListener("focus", refreshBadges);
    return () => { clearInterval(id); window.removeEventListener("focus", refreshBadges); };
  }, [isManager, refreshForgotten, refreshPending]);

  if (!session) {
    return <LoginScreen notice={notice} onLoggedIn={(s) => { setNotice(null); setTab("clock"); setSession(s); }} />;
  }

  const logout = () => { clearSession(); setSession(null); };

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">⏱️ Attendance Clock</div>
        <nav className="tabs" aria-label="Main">
          <button className={tab === "clock" ? "tab active" : "tab"} onClick={() => setTab("clock")}>Clock</button>
          <button className={tab === "week" ? "tab active" : "tab"} onClick={() => setTab("week")}>My week</button>
          {isManager && (
            <button className={tab === "team" ? "tab active" : "tab"} onClick={() => setTab("team")}>
              Team
              {forgottenCount > 0 && (
                <span className="tab-badge" title={`${forgottenCount} shift(s) open for more than 14 hours`}
                  aria-label={`${forgottenCount} forgotten shift(s)`}>{forgottenCount}</span>
              )}
            </button>
          )}
          {isManager && (
            <button className={tab === "requests" ? "tab active" : "tab"} onClick={() => setTab("requests")}>
              Requests
              {pendingCount > 0 && (
                <span className="tab-badge" title={`${pendingCount} correction request(s) waiting`}
                  aria-label={`${pendingCount} waiting request(s)`}>{pendingCount}</span>
              )}
            </button>
          )}
          {isManager && (
            <button className={tab === "reports" ? "tab active" : "tab"} onClick={() => setTab("reports")}>Reports</button>
          )}
        </nav>
        <div className="user">
          <span className="user-name">{session.employee.displayName}</span>
          <button className="link" onClick={logout}>Log out</button>
        </div>
      </header>
      <main className="content">
        {tab === "clock" ? <ClockScreen onViewAll={() => setTab("week")} />
          : tab === "week" ? <WeekScreen />
          : tab === "requests" ? <RequestsScreen onDataChanged={refreshPending} />
          : tab === "reports" ? <ReportsScreen />
          : <TeamScreen onDataChanged={refreshForgotten} />}
      </main>
    </div>
  );
}
