using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.History;
using AttendanceClock.Tests.Punches;

namespace AttendanceClock.Tests.History;

/// <summary>Plan 08 test table: my shifts and totals.</summary>
[Collection(DatabaseCollection.Name)]
public class HistoryTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private static Task<HistoryDto?> GetHistory(HttpClient client, string query) =>
        client.GetFromJsonAsync<HistoryDto>("/api/shifts/mine" + query);

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact] // 1
    public async Task Daily_and_range_totals_add_up()
    {
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-16T08:00:00+01:00", "2026-11-16T16:00:00+01:00"); // Mon 480
        await fixture.AddShiftAsync(id, "2026-11-17T09:00:00+01:00", "2026-11-17T12:00:00+01:00"); // Tue 180
        await fixture.AddShiftAsync(id, "2026-11-17T13:00:00+01:00", "2026-11-17T18:30:00+01:00"); // Tue 330

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-22");

        Assert.Equal(990, history!.TotalMinutes);
        Assert.Equal(3, history.Shifts.Count);
        Assert.Collection(history.Days,
            d => { Assert.Equal(new DateOnly(2026, 11, 16), d.Date); Assert.Equal(480, d.TotalMinutes); Assert.Equal(1, d.ShiftCount); },
            d => { Assert.Equal(new DateOnly(2026, 11, 17), d.Date); Assert.Equal(510, d.TotalMinutes); Assert.Equal(2, d.ShiftCount); });
    }

    [Fact] // 1b
    public async Task Each_shift_rounds_to_the_nearest_minute_and_totals_add_up()
    {
        // 1 min 40 s → 2 (up) · 1 min 20 s → 1 (down) · 0 min 30 s → 1 (half rounds up).
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-16T08:00:00+01:00", "2026-11-16T08:01:40+01:00");
        await fixture.AddShiftAsync(id, "2026-11-16T09:00:00+01:00", "2026-11-16T09:01:20+01:00");
        await fixture.AddShiftAsync(id, "2026-11-16T10:00:00+01:00", "2026-11-16T10:00:30+01:00");

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-16");

        Assert.Equal([2, 1, 1], history!.Shifts.Select(s => s.DurationMinutes!.Value));
        // What the employee sees adds up: the total is exactly the sum of the rows.
        Assert.Equal(history.Shifts.Sum(s => s.DurationMinutes), history.TotalMinutes);
        Assert.Equal(4, Assert.Single(history.Days).TotalMinutes);
    }

    [Fact] // 2 (D-10, EC-5)
    public async Task Overnight_shift_counts_only_on_its_start_date()
    {
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-18T22:00:00+01:00", "2026-11-19T06:00:00+01:00");

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-22");

        var day = Assert.Single(history!.Days);
        Assert.Equal(new DateOnly(2026, 11, 18), day.Date);
        Assert.Equal(480, day.TotalMinutes);
    }

    [Fact] // 3 (EC-6)
    public async Task Shift_starting_0030_on_the_DST_day_lands_on_that_day()
    {
        // 29 Mar 2026: clocks jump 02:00 → 03:00. 00:30 → 08:00 on the wall = 6.5 real hours.
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-03-29T00:30:00+01:00", "2026-03-29T08:00:00+02:00");

        var dstDay = await GetHistory(client, "?from=2026-03-29&to=2026-03-29");
        var dayBefore = await GetHistory(client, "?from=2026-03-28&to=2026-03-28");

        var day = Assert.Single(dstDay!.Days);
        Assert.Equal(new DateOnly(2026, 3, 29), day.Date);
        Assert.Equal(390, day.TotalMinutes);
        Assert.Empty(dayBefore!.Shifts);
    }

    [Fact] // 4
    public async Task Shifts_outside_the_range_are_not_included()
    {
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-15T23:30:00+01:00", "2026-11-16T01:00:00+01:00"); // starts Sunday before
        await fixture.AddShiftAsync(id, "2026-11-23T00:00:00+01:00", "2026-11-23T02:00:00+01:00"); // Monday after

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-22");

        Assert.Empty(history!.Shifts);
        Assert.Equal(0, history.TotalMinutes);
    }

    [Fact] // 5
    public async Task Another_employees_shifts_are_never_included()
    {
        var (dana, _) = await fixture.NewEmployeeClientAsync();
        var (_, yossiId) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(yossiId, "2026-11-16T08:00:00+01:00", "2026-11-16T16:00:00+01:00");

        var history = await GetHistory(dana, "?from=2026-11-16&to=2026-11-22");

        Assert.Empty(history!.Shifts);
    }

    [Fact] // 6
    public async Task Open_shift_is_returned_separately_and_not_counted()
    {
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-16T08:00:00+01:00", "2026-11-16T12:00:00+01:00"); // 240
        await fixture.AddShiftAsync(id, "2026-11-17T08:00:00+01:00");                             // still open

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-22");

        Assert.Equal(240, history!.TotalMinutes);
        Assert.Single(history.Shifts);
        Assert.NotNull(history.OpenShift);
        Assert.Equal(new DateOnly(2026, 11, 17), history.OpenShift!.ZurichDate);
    }

    [Fact] // 7
    public async Task No_dates_means_this_week_according_to_the_trusted_clock()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        fixture.Clock.SetNow("2026-11-19T10:00:00+01:00"); // a Thursday

        var history = await GetHistory(client, "");

        Assert.Equal(new DateOnly(2026, 11, 16), history!.From); // Monday
        Assert.Equal(new DateOnly(2026, 11, 22), history.To);    // Sunday
    }

    [Theory] // 8
    [InlineData("?from=2026-11-22&to=2026-11-16")] // from after to
    [InlineData("?from=2026-01-01&to=2026-12-31")] // more than 93 days
    [InlineData("?from=2026-11-16")]               // only one date
    public async Task Invalid_range_is_refused(string query)
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();

        var response = await client.GetAsync("/api/shifts/mine" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_RANGE", await ErrorCode(response));
    }

    [Fact] // 9
    public async Task History_requires_login()
    {
        using var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.GetAsync("/api/shifts/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // 10 (Plan 12)
    public async Task My_week_shows_each_shifts_work_location()
    {
        var (client, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2026-11-16T08:00:00+01:00", "2026-11-16T16:00:00+01:00", WorkLocation.Office);
        await fixture.AddShiftAsync(id, "2026-11-17T08:00:00+01:00", "2026-11-17T16:00:00+01:00", WorkLocation.Remote);
        await fixture.AddShiftAsync(id, "2026-11-18T08:00:00+01:00", "2026-11-18T16:00:00+01:00"); // before Plan 12

        var history = await GetHistory(client, "?from=2026-11-16&to=2026-11-22");

        Assert.Equal(["Office", "Remote", null], history!.Shifts.OrderBy(s => s.ClockIn).Select(s => s.WorkLocation));
    }
}
