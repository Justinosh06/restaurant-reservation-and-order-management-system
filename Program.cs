using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Navigation;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<SidebarNavService>();

builder.Services.AddRazorPages();
builder.Services.AddSingleton<IPageRouteModelProvider>(new AdminDomainPageRouteProvider("admin"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapGet("/api/info", () => "Main Domain API")
   .RequireHost("localhost");

app.MapGet("/api/info", () => "Admin Domain API")
   .RequireHost("admin.localhost");

app.MapRazorPages();

app.Run();