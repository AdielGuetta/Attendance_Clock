// Talks to the ASP.NET API. Every error comes back as ProblemDetails with a stable "code"
// (e.g. ALREADY_ON_SHIFT), which the screens turn into friendly messages.

import type { WorkLocation } from "./location";
import { clearSession, getSession } from "./session";

export type Employee = { id: number; userName: string; displayName: string; role: "Employee" | "Manager" };
export type LoginResponse = { token: string; expiresAtUtc: string; employee: Employee };

export type Shift = {
  id: number;
  zurichDate: string;
  clockIn: string;
  clockInSource: string;
  clockOut: string | null;
  clockOutSource: string | null;
  durationMinutes: number | null;
  flags: string[];
  workLocation: WorkLocation | null; // Plan 12; null = shift from before the feature
};
export type PunchStatus = { state: "OnShift" | "OffShift"; openShift: Shift | null };
export type DayTotal = { date: string; totalMinutes: number; shiftCount: number };
export type History = { from: string; to: string; totalMinutes: number; days: DayTotal[]; shifts: Shift[]; openShift: Shift | null };
export type TimeNow = { zurichTime: string; source: string };

// Manager views (Plan 10)
export type AdminShift = Shift & { employeeId: number; employeeName: string; rowVersion: string };
export type OnShift = { now: string | null; shifts: AdminShift[] };
export type EmployeeSummary = { id: number; userName: string; displayName: string; role: string; isActive: boolean };
export type ShiftAudit = { id: number; changedAt: string; changedBy: string; field: string; oldValue: string; newValue: string; reason: string };
export type CorrectShift = { clockIn?: string; clockOut?: string; reason: string; rowVersion: string };

export class ApiError extends Error {
  constructor(public status: number, public code: string | undefined, public title: string, public detail?: string) {
    super(detail ?? title);
  }
}

/** Called when the server says the login badge is no longer valid (expired, tampered, account deactivated). */
let onUnauthorized: () => void = () => {};
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const session = getSession();
  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(session ? { Authorization: `Bearer ${session.token}` } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, "NETWORK", "Can't reach the server. Check your connection and try again.");
  }

  if (response.ok) return (await response.json()) as T;

  let problem: { code?: string; title?: string; detail?: string } = {};
  try { problem = await response.json(); } catch { /* not JSON (e.g. an empty 401) */ }

  if (response.status === 401 && path !== "/api/auth/login") {
    clearSession();
    onUnauthorized();
  }
  throw new ApiError(response.status, problem.code, problem.title ?? `Request failed (${response.status})`, problem.detail);
}

export const api = {
  login: (userName: string, password: string) => request<LoginResponse>("POST", "/api/auth/login", { userName, password }),
  timeNow: () => request<TimeNow>("GET", "/api/time/now"),
  status: () => request<PunchStatus>("GET", "/api/punches/status"),
  // Only the location goes in the body, never a time: the server takes the time itself (D-2, Plan 12).
  clockIn: (location: WorkLocation) => request<Shift>("POST", "/api/punches/clock-in", { location }),
  clockOut: () => request<Shift>("POST", "/api/punches/clock-out"),
  history: (from?: string, to?: string) =>
    request<History>("GET", from && to ? `/api/shifts/mine?from=${from}&to=${to}` : "/api/shifts/mine"),

  admin: {
    employees: () => request<EmployeeSummary[]>("GET", "/api/admin/employees"),
    onShift: () => request<OnShift>("GET", "/api/admin/on-shift"),
    shifts: (from: string, to: string, employeeId?: number, flaggedOnly?: boolean) =>
      request<AdminShift[]>("GET", `/api/admin/shifts?from=${from}&to=${to}` +
        (employeeId ? `&employeeId=${employeeId}` : "") + (flaggedOnly ? "&flaggedOnly=true" : "")),
    audit: (shiftId: number) => request<ShiftAudit[]>("GET", `/api/admin/shifts/${shiftId}/audit`),
    correct: (shiftId: number, body: CorrectShift) => request<AdminShift>("PATCH", `/api/admin/shifts/${shiftId}`, body),
  },
};
