using System.Text;
using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var jwtSecret = builder.Configuration["Jwt:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "JWT secret is missing. Run: dotnet user-secrets set \"Jwt:Secret\" \"<32+ character secret>\" --project Banking_System.ApiService");
}

if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "JWT secret must be at least 32 bytes long.");
}

// Password-reset links point at the web app. The address comes from
// configuration, never from a request header, so a forged Host header cannot
// redirect reset links to another site.
var webBaseUrl = builder.Configuration["Web:BaseUrl"];

if (!Uri.TryCreate(webBaseUrl, UriKind.Absolute, out var webUri)
    || (webUri.Scheme != Uri.UriSchemeHttps && webUri.Scheme != Uri.UriSchemeHttp))
{
    throw new InvalidOperationException(
        "Web:BaseUrl must be the public address of the web app, e.g. https://bank.example.com");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    options.AddPolicy("EmployeeOrAdmin", policy =>
        policy.RequireRole("Employee", "Admin"));

    options.AddPolicy("CustomerOnly", policy =>
        policy.RequireRole("Customer"));
});

builder.Services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IBankCustomerRepository, BankCustomerRepository>();
builder.Services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
builder.Services.AddScoped<OtpService>();
// Real email goes out over SMTP once Smtp:Host is configured; until then
// notifications are only logged (Development) so nothing is silently "sent".
if (!string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
{
    builder.Services.AddScoped<INotificationSender, SmtpNotificationSender>();
}
else
{
    builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
}
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<AccountRecoveryService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
