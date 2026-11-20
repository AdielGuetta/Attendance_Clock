import { describe, expect, it } from "vitest";
import { loadLastLocation, locationLabel, saveLastLocation } from "./location";

/** A pretend localStorage, so the tests don't need a browser. */
const fakeStore = (initial: Record<string, string> = {}) => {
  const data = new Map(Object.entries(initial));
  return { getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v) };
};

describe("work location (Plan 12)", () => {
  it("labels Office and Remote, and shows a dash for old shifts without a location", () => {
    expect(locationLabel("Office")).toBe("🏢 Office");
    expect(locationLabel("Remote")).toBe("🏠 Remote");
    expect(locationLabel(null)).toBe("—");
    expect(locationLabel("Moon")).toBe("—");
  });

  it("remembers each user's last choice separately on a shared device", () => {
    const store = fakeStore();
    expect(loadLastLocation(2, store)).toBeNull(); // first time: nothing pre-selected, the employee must choose
    saveLastLocation(2, "Remote", store);
    expect(loadLastLocation(2, store)).toBe("Remote");
    expect(loadLastLocation(3, store)).toBeNull(); // the next person on this computer still chooses for themselves
  });

  it("ignores junk in storage and a blocked storage", () => {
    expect(loadLastLocation(2, fakeStore({ "attendance-clock.last-location.2": "office" }))).toBeNull();
    const blocked = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); } };
    expect(loadLastLocation(2, blocked)).toBeNull();
    expect(() => saveLastLocation(2, "Office", blocked)).not.toThrow();
  });
});
