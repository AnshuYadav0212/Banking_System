using Banking_System.Web;
using Banking_System.Web.Authentication;
using Banking_System.Web.Components;
using Banking_System.Web.Services.Authentication;
using Banking_System.Web.Services.Banking;
using Banking_System.Web.Services.Profile;
using Banking_System.Web.Services.Tickets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<AuthApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiservice");

});

builder.Services.AddHttpClient<BankingApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiservice");
});

builder.Services.AddHttpClient<TicketApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiservice");
});

builder.Services.AddHttpClient<ProfileApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiservice");
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BankingSystem.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The username availability check is public; cap it per client so it
    // cannot be used to enumerate accounts quickly.
    options.AddPolicy("username-check", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseOutputCache();

app.MapAuthEndpoints();
app.MapTicketEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
