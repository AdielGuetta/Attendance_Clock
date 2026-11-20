using System.Threading.RateLimiting;
using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Auth;
using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.History;
using AttendanceClock.Api.Punches;
using AttendanceClock.Api.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────────────────────────
// SQL Server via EF Core. The connection string is read when the database is first used (not at startup),
// so tests can point the app at a separate test database. In Docker it comes from docker-compose.yml.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")));

// ── Trusted clock (PRD §4) ────────────────────────────────────────────────────────────────────────
// External time APIs only. Registration ORDER = try order: primary first, then backup.
builder.Services.Configure<TimeProviderOptions>(builder.Configuration.GetSection(TimeProviderOptions.SectionName));
builder.Services.AddHttpClient<TimeApiIoSource>(ConfigureTimeHttpClient);
builder.Services.AddHttpClient<TimeNowSource>(ConfigureTimeHttpClient);
builder.Services.AddTransient<IExternalTimeSource>(sp => sp.GetRequiredService<TimeApiIoSource>());
builder.Services.AddTransient<IExternalTimeSource>(sp => sp.GetRequiredService<TimeNowSource>());
builder.Services.AddTransient<ITrustedClock, FallbackTrustedClock>();

// ── Clock In / Clock Out (Plan 07) ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<HistoryService>(); // Plan 08: my shifts + totals
builder.Services.AddScoped<AdminService>();   // Plan 10: manager views + corrections
builder.Services.AddScoped<AttendanceClock.Api.Requests.CorrectionRequestService>(); // Plan 13: employee correction requests
builder.Services.AddScoped<AttendanceClock.Api.Reports.ReportService>(); // Plan 15: manager reports + CSV export

// ── Login (Plan 06, PRD F1) ───────────────────────────────────────────────────────────────────────
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart(); // fail fast if the signing key is missing or too short
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwt, auth) =>
    {
        jwt.MapInboundClaims = false; // keep our short claim names ("sub", "role") as they are
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Value.Issuer,
            ValidAudience = auth.Value.Audience,
            IssuerSigningKey = TokenService.SigningKey(auth.Value),
            NameClaimType = TokenService.Claims.UserName,
            RoleClaimType = TokenService.Claims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Brute-force protection: N login attempts per minute per IP address, then 429.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.AddPolicy(AuthController.LoginRateLimitPolicy, http =>
    {
        var perMinute = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.LoginAttemptsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many login attempts. Please wait a minute and try again.",
            Extensions = { ["code"] = "TOO_MANY_ATTEMPTS" },
        }, options: null, contentType: "application/problem+json", cancellationToken);
    };
});

// ── Errors, controllers, health ───────────────────────────────────────────────────────────────────
// Errors as ProblemDetails (RFC 9457); "time service down" becomes a clear 503.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TimeSourceUnavailableExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // https://aka.ms/aspnet/openapi

// Health checks: "is the server alive, and can it reach the database?"
// Later: a check for the external time providers.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development convenience: create/upgrade the tables and add demo accounts on startup,
    // so a fresh clone works with just "docker compose up".
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await DevDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Employee>>());
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

static void ConfigureTimeHttpClient(HttpClient client)
{
    // The per-attempt timeout is enforced by FallbackTrustedClock; this is only a safety net.
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AttendanceClock/1.0");
}

// Lets the test project start the app in memory (WebApplicationFactory).
public partial class Program;
