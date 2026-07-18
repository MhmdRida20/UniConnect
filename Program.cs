// Program.cs
//
// Key points:
//   1. We use ApplicationUser instead of IdentityUser everywhere
//   2. Email confirmation is required for real (see SmtpEmailSender)
//   3. After build(), DbSeeder.SeedAsync provisions roles/accounts and syncs
//      the default university's data through the (simulated) external API —
//      there is no mock data path anywhere in this project.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Hubs;
using UniConnect.Services;
var builder = WebApplication.CreateBuilder(args);

// --- Database ---------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// --- Identity ---------------------------------------------------------------
// Note: ApplicationUser (NOT IdentityUser) � this is critical
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true; // now enforced for real — see SmtpEmailSender
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// FR-92 audit logging: "User login" — hooked here rather than in a
// scaffolded Login page, since ASP.NET Core Identity's sign-in cookie
// fires this event on every successful login regardless of which page
// triggered it, with zero need to touch/scaffold the default Login page.
// Failed logins ARE hooked at the page level — see Login.cshtml.cs — since
// there's no equivalent built-in event for "sign-in was attempted and failed."
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnSignedIn = async context =>
    {
        var auditLog = context.HttpContext.RequestServices.GetRequiredService<UniConnect.Services.AuditLogService>();
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.Principal!);
        if (user is not null)
            await auditLog.LogAsync("Login", userId: user.Id, universityCode: user.UniversityCode, entityType: "User", entityId: user.Id);
    };
});

// Overrides the no-op IEmailSender that AddDefaultIdentity() registers by
// default (which is why email confirmation silently did nothing before).
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, UniConnect.Services.SmtpEmailSender>();

// Auth Edge Cases: "Role change during session — the system shall update
// the service menu on the next request or session refresh." ASP.NET Core
// Identity re-validates the auth cookie against the DB at this interval and
// refreshes the user's claims (including roles) if anything changed. The
// framework default is 30 minutes; shortened here so a role/permission
// change takes effect within about a minute instead of up to half an hour.
builder.Services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// ---------- Real API integration (Attendance/instructor feedback follow-up) ----------
// The simulated external university system (see /Controllers/ExternalApi and
// /ExternalApi/ExternalUniversityDataStore.cs for why this exists). Scoped,
// not Singleton, since it's now backed by the real database (ApplicationDbContext
// is itself scoped) — this is what makes its data survive app restarts.
builder.Services.AddScoped<UniConnect.ExternalApi.ExternalUniversityDataStore>();

// Named HttpClient used by both RealApiUniversityProvider (on-demand calls)
// and UniversityApiSyncRunner (the periodic sync job) to call a university's API.
builder.Services.AddHttpClient("UniversityApi");

builder.Services.AddScoped<UniConnect.Services.UniversityApiSyncRunner>();
builder.Services.AddScoped<UniConnect.Services.EnrollmentRevalidationRunner>();
builder.Services.AddScoped<UniConnect.Services.NotificationService>();
builder.Services.AddScoped<UniConnect.Services.MatchingScoreService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UniConnect.Services.AuditLogService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddHostedService<UniConnect.Services.InactiveStudyGroupService>();
builder.Services.AddHostedService<UniConnect.Services.EnrollmentRevalidationService>();
builder.Services.AddHostedService<UniConnect.Services.InactiveClubService>();
builder.Services.AddHostedService<UniConnect.Services.CloseExpiredAttendanceSessionsService>();
builder.Services.AddHostedService<UniConnect.Services.TicketStalenessService>();
builder.Services.AddHostedService<UniConnect.Services.UniversityApiSyncService>();
// Geocoding service (text address → coordinates) using a typed HttpClient
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>();

// Multi-university adapter core — see /Adapters/IUniversityProvider.cs
builder.Services.AddScoped<UniConnect.Adapters.RealApiUniversityProvider>();
builder.Services.AddScoped<UniConnect.Adapters.IUniversityProviderResolver, UniConnect.Adapters.UniversityProviderResolver>();
builder.Services.AddScoped<UniConnect.Services.IServiceCatalogService, UniConnect.Services.ServiceCatalogService>();

var app = builder.Build();

// --- Apply migrations and seed at startup -----------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var externalStore = scope.ServiceProvider.GetRequiredService<UniConnect.ExternalApi.ExternalUniversityDataStore>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await db.Database.MigrateAsync();   // applies any pending migrations
    await DbSeeder.SeedAsync(db, roleManager, userManager, externalStore, config);
}

// --- Standard pipeline ------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<UniConnect.Middleware.SuspendedUserMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.MapHub<StudyGroupHub>("/studygroupHub");
app.MapHub<RideTrackingHub>("/rideTrackingHub");
app.MapHub<TicketHub>("/ticketHub");
app.MapHub<ClubHub>("/clubHub");
app.MapHub<AttendanceHub>("/attendanceHub");
app.MapHub<NotificationHub>("/notificationHub");

app.Run();
