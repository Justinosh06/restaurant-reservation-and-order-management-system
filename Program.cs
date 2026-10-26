using Stripe;
using Stripe.Checkout;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Announcements;
using RestaurantReservation.Infrastructure.Email;
using RestaurantReservation.Infrastructure.Navigation;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Infrastructure.Stripe;

using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind and Register Stripe Settings
var stripeSettings = builder.Configuration.GetSection("Stripe").Get<StripeSettings>();
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));

// Set global API key for Stripe.net
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"] ?? stripeSettings?.SecretKey;

// Register Stripe Services for Dependency Injection
builder.Services.AddScoped<SessionService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<SidebarNavService>();
builder.Services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddSingleton<RestaurantClock>();
builder.Services.AddScoped<RestaurantCurrency>();
builder.Services.AddScoped<RestaurantOrderingHours>();
builder.Services.AddScoped<DiningSessionService>();
builder.Services.AddScoped<ImageUploadService>();
builder.Services.AddScoped<AdminPortalService>();
builder.Services.AddScoped<CustomerNotificationService>();
builder.Services.AddScoped<AnalyticsReportBuilder>();
builder.Services.AddScoped<PeakHoursAnalytics>();

// The dashboard's PDF export uses QuestPDF, which is free under its Community licence for a project like this.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Announcement emails (sent to Mailpit locally; see the "Email" section of appsettings.json).
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<AnnouncementEmailQueue>();
builder.Services.AddHostedService<AnnouncementEmailWorker>();

builder.Services.AddScoped<IPaymentService, StripePaymentService>();

// Payment details from Stripe on the customer's dining visits and reservations, cached in memory.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RestaurantReservation.Infrastructure.Payments.StripePaymentDetails>();

// Register Controllers (Required for Stripe Webhook Controller)
builder.Services.AddControllers();

// 2. Configure HTTP Pipeline
// Login / Register
builder.Services.AddScoped<IPasswordHasher<RestaurantReservation.Models.Customer>, PasswordHasher<RestaurantReservation.Models.Customer>>();
builder.Services.AddSingleton<CaptchaService>();
builder.Services.AddSingleton<AdminSignInTicketService>();

// Email (login codes). In development the mail goes to Mailpit: SMTP on localhost:1025, inbox at http://localhost:8025.
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<LoginCodeService>();

// Session stores the current CAPTCHA answer on the server side.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Cookie login with the role as a claim. The login page only exists on the main site,
// so the admin subdomain sends anonymous users back to the main site's /Login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect(SiteHosts.IsAdminHost(context.Request)
                ? SiteHosts.MainSiteUrl(context.Request, "/Login")
                : context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect(SiteHosts.IsAdminHost(context.Request)
                ? SiteHosts.MainSiteUrl(context.Request, "/AccessDenied")
                : context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPortal", policy => policy.RequireRole(AppRoles.Administrator, AppRoles.Staff));
    options.AddPolicy("AdministratorOnly", policy => policy.RequireRole(AppRoles.Administrator));
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole(AppRoles.Customer));
});

// Razor Pages, plus the route provider that keeps /Admin pages on the admin subdomain (registered once only:
// a second copy adds a second "/" route to Admin/Index and every request to it fails as ambiguous).
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminPortal");
    options.Conventions.AllowAnonymousToPage("/Admin/SignIn");

    options.Conventions.AuthorizeFolder("/Admin/Dashboard", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Admin/Staffs", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Admin/Announcements", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Admin/Promotions", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Admin/Menu", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Admin/Settings", "AdministratorOnly");
    options.Conventions.AuthorizeFolder("/Customer", "CustomerOnly");
});
// Custom subdomain route provider. Register once: a second copy maps Admin/Index to "/" twice.
builder.Services.AddSingleton<IPageRouteModelProvider>(new AdminDomainPageRouteProvider(SiteHosts.AdminSubdomain));

var app = builder.Build();

await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Handles serving CSS, JS, and image files from wwwroot
app.UseStaticFiles();

app.UseRouting();
app.Use(async (context, next) =>
{
    if (SiteHosts.IsAdminHost(context.Request))
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }

    await next();
});
app.UseStatusCodePagesWithReExecute("/NotFound");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Customers without a name or phone number complete their profile before anything else.
app.UseProfileSetup();

// 3. Minimal API endpoints filtered by host header
app.MapGet("/api/info", () => "Main Domain API")
   .RequireHost("localhost");

app.MapGet("/api/info", () => "Admin Domain API")
   .RequireHost("admin.localhost");

// 4. Map API Controllers (For Stripe Webhooks) and Razor Pages
app.MapControllers();
app.MapRazorPages();

app.Run();
