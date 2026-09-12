using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OnlineBooking.Api.Configuration;
using OnlineBooking.Api.Data;
using OnlineBooking.Api.Endpoints;
using OnlineBooking.Api.Middleware;
using OnlineBooking.Api.Repositories;
using OnlineBooking.Api.Services;
using OnlineBooking.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration fortement typée ---
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection(BookingOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

// --- Accès aux données : NpgsqlDataSource fournit un pool de connexions ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default manquante.");
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddScoped<BookingRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AuthService>();

// --- Authentification JWT (Req 8) ---
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Section Jwt manquante.");

// 🔴 FIX 4 : Valider que JWT Secret est fourni (pas vide dev)
if (string.IsNullOrWhiteSpace(jwt.Secret))
{
    throw new InvalidOperationException(
        "Jwt:Secret est vide. Utiliser:\n" +
        "  Dev: dotnet user-secrets set \"Jwt:Secret\" \"your-long-random-secret\"\n" +
        "  Prod: Variable d'env JWT_SECRET");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// --- Tâche de fond : expiration des holds (Req 4.3) ---
builder.Services.AddHostedService<HoldExpirationWorker>();

var app = builder.Build();

// --- Migrations automatiques au démarrage (Req 10) ---
var migrationsDir = Path.Combine(AppContext.BaseDirectory, "migrations");
using (var scope = app.Services.CreateScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Migrator>>();
    var migrator = new Migrator(dataSource, migrationsDir, logger);
    await migrator.MigrateAsync();
}

// --- Pipeline (ordre important) ---
app.UseMiddleware<ErrorHandlingMiddleware>();  // capture toutes les exceptions
app.UseAuthentication();                        // renseigne context.User
app.UseMiddleware<RateLimitMiddleware>();       // limite par utilisateur/IP (Req 9.3)
app.UseAuthorization();

// --- Endpoint de santé par nœud (Req 11.1) ---
app.MapGet("/health", () => Results.Ok(new { status = "ok", node = Environment.MachineName }));

app.MapApiEndpoints();

app.Run();

// Rend le point d'entrée accessible aux tests d'intégration.
public partial class Program { }
