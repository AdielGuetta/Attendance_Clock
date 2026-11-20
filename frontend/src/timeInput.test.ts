import { describe, expect, it } from "vitest";
import { HOURS, joinDateTime, MINUTES, normalizeTime } from "./timeInput";

describe("24-hour time entry", () => {
  it("accepts 24-hour times in a few shapes and writes them as HH:mm", () => {
    expect(normalizeTime("17:00")).toBe("17:00");
    expect(normalizeTime("7:05")).toBe("07:05");
    expect(normalizeTime("1700")).toBe("17:00");
    expect(normalizeTime("705")).toBe("07:05");
    expect(normalizeTime(" 00:00 ")).toBe("00:00");
    expect(normalizeTime("23:59")).toBe("23:59");
  });

  it("refuses anything that isn't a real 24-hour time (no AM/PM)", () => {
    for (const bad of ["24:00", "25:00", "17:60", "5pm", "05:00 PM", "17", "", "ab:cd"]) {
      expect(normalizeTime(bad)).toBeNull();
    }
  });

  it("joins date and time, and tells 'empty' apart from 'not finished'", () => {
    expect(joinDateTime("2026-11-18", "17:00")).toBe("2026-11-18T17:00");
    expect(joinDateTime("2026-11-18", "7:05")).toBe("2026-11-18T07:05");
    expect(joinDateTime("", "")).toBe("");              // no value (e.g. still open)
    expect(joinDateTime("2026-11-18", "")).toBeNull();  // time missing
    expect(joinDateTime("", "17:00")).toBeNull();       // date missing
    expect(joinDateTime("2026-11-18", "25:00")).toBeNull();
  });

  it("offers every hour 00–23 and every minute 00–59 in the roller", () => {
    expect(HOURS).toHaveLength(24);
    expect([HOURS[0], HOURS[23]]).toEqual(["00", "23"]);
    expect(MINUTES).toHaveLength(60);
    expect([MINUTES[0], MINUTES[59]]).toEqual(["00", "59"]);
  });
});
