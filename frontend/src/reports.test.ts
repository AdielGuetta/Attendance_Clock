import { describe, expect, it } from "vitest";
import { barPercent, datesBetween, mondayOf, presetPeriod, reportFileName } from "./reports";

describe("manager reports (Plan 15)", () => {
  it("this week runs Monday to Sunday", () => {
    expect(presetPeriod("thisWeek", "2026-11-20")).toEqual({ from: "2026-11-16", to: "2026-11-22" }); // a Friday
    expect(mondayOf("2026-11-22")).toBe("2026-11-16"); // Sunday belongs to the week before
    expect(mondayOf("2026-11-16")).toBe("2026-11-16");
  });

  it("this month and last month, including short months and leap years", () => {
    expect(presetPeriod("thisMonth", "2026-11-20")).toEqual({ from: "2026-11-01", to: "2026-11-30" });
    expect(presetPeriod("lastMonth", "2026-11-20")).toEqual({ from: "2026-10-01", to: "2026-10-31" });
    expect(presetPeriod("lastMonth", "2028-03-05")).toEqual({ from: "2028-02-01", to: "2028-02-29" }); // leap year
  });

  it("last month in January is December of the year before", () => {
    expect(presetPeriod("lastMonth", "2027-01-10")).toEqual({ from: "2026-12-01", to: "2026-12-31" });
    expect(presetPeriod("thisMonth", "2026-12-31")).toEqual({ from: "2026-12-01", to: "2026-12-31" });
  });

  it("lists every day of the range, names the file like the server, and scales the bars", () => {
    expect(datesBetween("2026-10-30", "2026-11-02")).toEqual(["2026-10-30", "2026-10-31", "2026-11-01", "2026-11-02"]);
    expect(reportFileName({ from: "2026-11-01", to: "2026-11-30" })).toBe("attendance-2026-11-01_2026-11-30.csv");
    expect(barPercent(240, 480)).toBe(50);
    expect(barPercent(0, 0)).toBe(0);
  });
});
