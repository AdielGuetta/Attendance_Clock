using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceClock.Api.Auth;
using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;
using AttendanceClock.Api.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AttendanceClock.Tests.Punches;

/// <summary>Starts the app once with a fake clock swapped in for the external time APIs.</summary>
public sealed class PunchFixture : IDisposable
{
    public FakeTrustedClock Clock { get; } = new();
    public WebApplicationFactory<Program> Factory { get; }
    private readonly ApiFactory _baseFactory;

    public PunchFixture()
    {
        ApiFactory.ResetDatabase();
        _baseFactory = new ApiFactory();
        Factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedClock>();
            services.AddSingleton<ITrustedClock>(Clock);
        }));
        Factory.CreateClient().Dispose(); // start the app → migrations + demo accounts
    }

    /// <summary>Creates a fresh employee (so tests don't affect each other) and an HTTP client logged in as them.</summary>
    public async Task<(HttpClient Client, int EmployeeId)> NewEmployeeClientAsync(EmployeeRole role = EmployeeRole.Employee)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee { UserName = "emp-" + Guid.NewGuid().ToString("N")[..12], DisplayName = "Test " + role, Role = role };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(employee);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, employee.Id);
    }

    /// <summary>Puts a shift straight into the test database (for history tests that need shifts on many days).</summary>
    /// <param name="location">Null = a shift recorded before Plan 12 (no location known).</param>
    public async Task<long> AddShiftAsync(int employeeId, string clockIn, string? clockOut = null, WorkLocation? location = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shift = new Shift
        {
            EmployeeId = employeeId,
            ClockIn = DateTimeOffset.Parse(clockIn),
            ClockInSource = "test-data",
            ClockOut = clockOut is null ? null : DateTimeOffset.Parse(clockOut),
            ClockOutSource = clockOut is null ? null : "test-data",
            WorkLocation = location,
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();
        return shift.Id;
    }

    public async Task<int> CountShiftsAsync(int employeeId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Shifts.CountAsync(s => s.EmployeeId == employeeId);
    }

    public void Dispose()
    {
        Factory.Dispose();
        _baseFactory.Dispose();
    }
}

/// <summary>Plan 07 test table.</summary>
[Collection(DatabaseCollection.Name)]
public class PunchTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private FakeTrustedClock Clock => fixture.Clock;

    // Since Plan 12 Clock In carries the work location (and only that: never a time).
    private static Task<HttpResponseMessage> ClockIn(HttpClient c, string location = "Office") =>
        c.PostAsJsonAsync("/api/punches/clock-in", new { location });

    private static Task<HttpResponseMessage> ClockInRawJson(HttpClient c, string? json) =>
        c.PostAsync("/api/punches/clock-in",
            json is null ? null : new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    private static Task<HttpResponseMessage> ClockOut(HttpClient c) => c.PostAsync("/api/punches/clock-out", null);

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact] // 1
    public async Task Clock_in_records_the_time_from_the_trusted_clock()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T08:02:11+01:00");

        var response = await ClockIn(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shift = await response.Content.ReadFromJsonAsync<ShiftDto>();
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T08:02:11+01:00"), shift!.ClockIn);
        Assert.Equal(TimeSpan.FromHours(1), shift.ClockIn.Offset); // Zurich offset kept
        Assert.Equal("fake-clock", shift.ClockInSource);
        Assert.Null(shift.ClockOut);
    }

    [Fact] // 2
    public async Task Clocking_in_twice_is_refused_and_says_since_when()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T08:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2026-11-20T08:05:00+01:00");
        var second = await ClockIn(client);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ALREADY_ON_SHIFT", await ErrorCode(second));
        Assert.Contains("2026-11-20 08:00", await second.Content.ReadAsStringAsync());
    }

    [Fact] // 3
    public async Task Clocking_out_while_off_shift_is_refused()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T17:00:00+01:00");

        var response = await ClockOut(client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NOT_ON_SHIFT", await ErrorCode(response));
    }

    [Fact] // 4
    public async Task Normal_day_in_0800_out_1630_is_510_minutes_and_status_returns_to_off_shift()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T08:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2026-11-20T16:30:00+01:00");
        var response = await ClockOut(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(510, (await response.Content.ReadFromJsonAsync<ShiftDto>())!.DurationMinutes);
        var status = await client.GetFromJsonAsync<PunchStatusDto>("/api/punches/status");
        Assert.Equal("OffShift", status!.State);
        Assert.Null(status.OpenShift);
    }

    [Fact] // 5 (EC-5)
    public async Task Overnight_shift_2200_to_0600_is_480_minutes()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T22:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2026-11-21T06:00:00+01:00");
        var shift = await (await ClockOut(client)).Content.ReadFromJsonAsync<ShiftDto>();

        Assert.Equal(480, shift!.DurationMinutes);
    }

    [Fact] // 6 (EC-6)
    public async Task Night_the_clocks_jump_forward_counts_7_hours_not_8()
    {
        // 28 Mar 2027 at 02:00 CET the clocks jump to 03:00 CEST. 22:00 → 06:00 on the wall = 7 real hours.
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2027-03-27T22:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2027-03-28T06:00:00+02:00");
        var shift = await (await ClockOut(client)).Content.ReadFromJsonAsync<ShiftDto>();

        Assert.Equal(420, shift!.DurationMinutes);
    }

    [Fact] // 7 (D-5, EC-8)
    public async Task Time_service_down_means_nothing_is_saved()
    {
        var (client, employeeId) = await fixture.NewEmployeeClientAsync();
        Clock.SimulateOutage();

        var response = await ClockIn(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("TIME_SOURCE_UNAVAILABLE", await ErrorCode(response));
        Assert.Equal(0, await fixture.CountShiftsAsync(employeeId));
    }

    [Fact] // 8 (D-9, EC-1/2)
    public async Task Ten_simultaneous_clock_ins_create_exactly_one_shift()
    {
        var (client, employeeId) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T08:00:00+01:00");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ClockIn(client)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await fixture.CountShiftsAsync(employeeId));
    }

    [Fact] // 9
    public async Task Punching_without_logging_in_is_refused()
    {
        using var anonymous = fixture.Factory.CreateClient();

        var response = await ClockIn(anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // 10 (F7)
    public async Task Fifteen_hour_shift_is_recorded_and_flagged_too_long()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T06:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2026-11-20T21:00:00+01:00");
        var shift = await (await ClockOut(client)).Content.ReadFromJsonAsync<ShiftDto>();

        Assert.Equal(900, shift!.DurationMinutes);
        Assert.Contains("TooLong", shift.Flags);
    }

    [Fact] // 11 (EC-9)
    public async Task Clock_out_time_before_clock_in_is_rejected_and_shift_stays_open()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T10:00:00+01:00");
        await ClockIn(client);

        Clock.SetNow("2026-11-20T09:59:00+01:00"); // provider answers with an EARLIER time
        var response = await ClockOut(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("TIME_SOURCE_INCONSISTENT", await ErrorCode(response));
        var status = await client.GetFromJsonAsync<PunchStatusDto>("/api/punches/status");
        Assert.Equal("OnShift", status!.State);
    }

    [Fact] // 12 (F3)
    public async Task Status_while_on_shift_shows_since_when()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T07:45:00+01:00");
        await ClockIn(client);

        var status = await client.GetFromJsonAsync<PunchStatusDto>("/api/punches/status");

        Assert.Equal("OnShift", status!.State);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T07:45:00+01:00"), status.OpenShift!.ClockIn);
    }

    [Fact] // 13 (Plan 12)
    public async Task Work_location_is_saved_with_the_shift_and_kept_after_clock_out()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T08:00:00+01:00");

        var clockIn = await ClockIn(client, "remote"); // any letter case is accepted
        var status = await client.GetFromJsonAsync<PunchStatusDto>("/api/punches/status");
        Clock.SetNow("2026-11-20T12:00:00+01:00");
        var closed = await (await ClockOut(client)).Content.ReadFromJsonAsync<ShiftDto>();

        Assert.Equal(HttpStatusCode.Created, clockIn.StatusCode);
        Assert.Equal("Remote", (await clockIn.Content.ReadFromJsonAsync<ShiftDto>())!.WorkLocation);
        Assert.Equal("Remote", status!.OpenShift!.WorkLocation);
        Assert.Equal("Remote", closed!.WorkLocation);
    }

    [Theory] // 14 (Plan 12)
    [InlineData(null)]                    // no body at all
    [InlineData("{}")]                    // body without a location
    [InlineData("""{"location":""}""")]
    [InlineData("""{"location":"Moon"}""")]
    [InlineData("""{"location":"1"}""")]  // a number would slip through a plain Enum.TryParse
    public async Task Missing_or_unknown_location_is_refused_before_calling_the_time_api(string? json)
    {
        var (client, employeeId) = await fixture.NewEmployeeClientAsync();
        Clock.SimulateOutage(); // if the time API were called, we'd get 503 instead of 400

        var response = await ClockInRawJson(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_LOCATION", await ErrorCode(response));
        Assert.Equal(0, await fixture.CountShiftsAsync(employeeId));
    }

    [Fact] // 15 (Plan 12, D-2)
    public async Task A_time_sent_by_the_client_is_ignored()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        Clock.SetNow("2026-11-20T09:00:00+01:00");

        var response = await ClockInRawJson(client, """{"location":"Office","clockIn":"2020-01-01T06:00:00+01:00"}""");

        var shift = await response.Content.ReadFromJsonAsync<ShiftDto>();
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T09:00:00+01:00"), shift!.ClockIn); // the trusted clock's time
        Assert.Equal("Office", shift.WorkLocation);
    }
}
