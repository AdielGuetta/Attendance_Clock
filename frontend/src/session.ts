// Keeps the login badge (JWT) between page refreshes. Trade-off (docs/plans/09-react-ui.md): localStorage is readable by
// scripts on the page, so an XSS hole could steal it. We never render raw HTML (React escapes everything),
// and the badge expires after 60 minutes anyway.

import type { Employee } from "./api";

const KEY = "attendance-clock.session";

export type Session = { token: string; expiresAtUtc: string; employee: Employee };

export function getSession(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    // Only a convenience check: the server is the real judge of expiry.
    return new Date(session.expiresAtUtc).getTime() > Date.now() ? session : null;
  } catch {
    return null;
  }
}

export function saveSession(session: Session) {
  try { localStorage.setItem(KEY, JSON.stringify(session)); } catch { /* private mode: stays logged in for this page only */ }
}

export function clearSession() {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
}
