// Work location (Plan 12, proj2): Office or Remote, chosen before Clock In and stored with the shift.
// Kept apart from the screens so it's easy to test.

export type WorkLocation = "Office" | "Remote";

export const WORK_LOCATIONS: readonly WorkLocation[] = ["Office", "Remote"];

const ICONS: Record<WorkLocation, string> = { Office: "🏢", Remote: "🏠" };

/** "🏢 Office" / "🏠 Remote". Shifts from before this feature have no location: shown as "—" (we don't guess). */
export function locationLabel(location: string | null | undefined): string {
  return isWorkLocation(location) ? `${ICONS[location]} ${location}` : "—";
}

export function isWorkLocation(value: unknown): value is WorkLocation {
  return value === "Office" || value === "Remote";
}

// Each user's last choice is remembered on this device, so tomorrow it's already selected. Per user, not per
// device: on a shared computer, one person's "Remote" must not be pre-selected for the next person.
// Only a convenience: if storage is blocked or holds junk, the employee simply picks again.
const keyFor = (employeeId: number) => `attendance-clock.last-location.${employeeId}`;
type Store = Pick<Storage, "getItem" | "setItem">;
const browserStore = (): Store | undefined => (typeof localStorage === "undefined" ? undefined : localStorage);

export function loadLastLocation(employeeId: number, store: Store | undefined = browserStore()): WorkLocation | null {
  try {
    const value = store?.getItem(keyFor(employeeId));
    return isWorkLocation(value) ? value : null;
  } catch {
    return null;
  }
}

export function saveLastLocation(employeeId: number, location: WorkLocation, store: Store | undefined = browserStore()) {
  try { store?.setItem(keyFor(employeeId), location); } catch { /* private mode: just not remembered */ }
}
