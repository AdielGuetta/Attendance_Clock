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

// Correction requests (Plan 13). Requested times are null when the employee keeps the recorded one.
export type RequestStatus = "Pending" | "Approved" | "Rejected";
export type CorrectionRequest = {
  id: number; shiftId: number; employeeId: number; employeeName: string; status: RequestStatus;
  requestedClockIn: string | null; requestedClockOut: string | null; reason: string; createdAt: string;
  decidedBy: string | null; decidedAt: string | null;
  shift: Shift; // the shift's CURRENT recorded times, for comparison
};
/** Zurich wall times like "2026-11-18T17:00"; only the changed ones are sent. */
export type NewCorrectionRequest = { clockIn?: string; clockOut?: string; reason: string };

// Manager reports (Plan 15). Minutes are closed shifts only, each rounded to the nearest minute, then summed.
export type ReportTotals = {
  totalMinutes: number; shifts: number; employeesWorked: number;
  officeMinutes: number; remoteMinutes: number; unknownLocationMinutes: number;
  overLimitShifts: number; correctedShifts: number; openShifts: number; pendingRequests: number;
};
export type EmployeeReport = {
  employeeId: number; name: string; totalMinutes: number; shifts: number;
  officeMinutes: number; remoteMinutes: number; unknownLocationMinutes: number;
  overLimitShifts: number; correctedShifts: number; openShifts: number;
};
export type DayReport = { date: string; totalMinutes: number; shifts: number };
export type Report = { from: string; to: string; employeeId: number | null; totals: ReportTotals; employees: EmployeeReport[]; days: DayReport[] };

const reportQuery = (from: string, to: string, employeeId?: number) =>
  `from=${from}&to=${to}` + (employeeId ? `&employeeId=${employeeId}` : "");

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

/** Sends a request with the login badge; returns the response when it's OK, otherwise throws an ApiError. */
async function send(method: string, path: string, body?: unknown): Promise<Response> {
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
  if (!response.ok) await fail(response, path);
  return response;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  return (await (await send(method, path, body)).json()) as T;
}

/**
 * A file from the server (Plan 15: the CSV report). A plain <a href> link can't carry the login badge (it travels in
 * a request header), so we fetch the file ourselves and hand it to the browser as a "blob" to save.
 */
async function download(path: string): Promise<Blob> {
  return (await send("GET", path)).blob();
}

async function fail(response: Response, path: string): Promise<never> {
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
  // The shift this screen shows (Plan 14): if a different one is open now (old tab), the server refuses: SHIFT_CHANGED.
  clockOut: (shiftId: number) => request<Shift>("POST", "/api/punches/clock-out", { shiftId }),
  history: (from?: string, to?: string) =>
    request<History>("GET", from && to ? `/api/shifts/mine?from=${from}&to=${to}` : "/api/shifts/mine"),
  requestCorrection: (shiftId: number, body: NewCorrectionRequest) =>
    request<CorrectionRequest>("POST", `/api/shifts/${shiftId}/correction-requests`, body),
  myRequests: () => request<CorrectionRequest[]>("GET", "/api/correction-requests/mine"),

  admin: {
    employees: () => request<EmployeeSummary[]>("GET", "/api/admin/employees"),
    onShift: () => request<OnShift>("GET", "/api/admin/on-shift"),
    shifts: (from: string, to: string, employeeId?: number, flaggedOnly?: boolean) =>
      request<AdminShift[]>("GET", `/api/admin/shifts?from=${from}&to=${to}` +
        (employeeId ? `&employeeId=${employeeId}` : "") + (flaggedOnly ? "&flaggedOnly=true" : "")),
    audit: (shiftId: number) => request<ShiftAudit[]>("GET", `/api/admin/shifts/${shiftId}/audit`),
    correct: (shiftId: number, body: CorrectShift) => request<AdminShift>("PATCH", `/api/admin/shifts/${shiftId}`, body),
    requests: (status?: RequestStatus) =>
      request<CorrectionRequest[]>("GET", "/api/admin/correction-requests" + (status ? `?status=${status}` : "")),
    approve: (id: number) => request<CorrectionRequest>("POST", `/api/admin/correction-requests/${id}/approve`),
    reject: (id: number) => request<CorrectionRequest>("POST", `/api/admin/correction-requests/${id}/reject`),
    report: (from: string, to: string, employeeId?: number) =>
      request<Report>("GET", `/api/admin/reports?${reportQuery(from, to, employeeId)}`),
    exportReport: (from: string, to: string, employeeId?: number) =>
      download(`/api/admin/reports/export?${reportQuery(from, to, employeeId)}`),
  },
};
