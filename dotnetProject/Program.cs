using dotnetProject.Data;
using dotnetProject.Hubs;
using dotnetProject.Middleware;
using dotnetProject.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

// Add SignalR
builder.Services.AddSignalR();

// Add Database Context - Using SQLite for simplicity (you can change to SQL Server)
builder.Services.AddDbContext<CasinoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=casino.db"));

// Add Wallet Service
builder.Services.AddScoped<IWalletService, WalletService>();

// Add Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CasinoDbContext>();
    context.Database.EnsureCreated(); // Creates database if it doesn't exist
    // For production, use migrations: context.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Add Player Identification Middleware (BEFORE Session)
app.UsePlayerIdentification();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Lobby}/{action=Index}/{id?}");

// Map SignalR hubs
app.MapHub<RouletteHub>("/hubs/roulette");
app.MapHub<PokerHub>("/hubs/poker");

app.Run();