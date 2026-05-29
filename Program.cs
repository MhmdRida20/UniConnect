
// Program.cs
//
// Replace your existing Program.cs with this version. The key additions over the
// default template are:
//   1. We use ApplicationUser instead of IdentityUser everywhere
//   2. We disable RequireConfirmedAccount for local dev (so signup works without email)
//   3. After build(), we call DbSeeder.SeedAsync to insert mock students/courses/enrollments

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Hubs;

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
        options.SignIn.RequireConfirmedAccount = false; // disable email confirmation for dev
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

var app = builder.Build();

// --- Apply migrations and seed mock data at startup -------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();   // applies any pending migrations
    await DbSeeder.SeedAsync(db);       // inserts mock students/courses/enrollments
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
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.MapHub<StudyGroupHub>("/studygroupHub");

app.Run();
