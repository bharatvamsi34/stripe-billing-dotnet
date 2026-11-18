using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StripeBilling.Api.Infrastructure;
using StripeBilling.Application;
using StripeBilling.Infrastructure;
using StripeBilling.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<BillingDbContext>("database");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Stripe Billing API", Version = "v1" });
    o.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = ApiKeyMiddleware.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }] = [],
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithHeaders(ApiKeyMiddleware.HeaderName, IdempotentAttribute.HeaderName, "Content-Type")
    .WithMethods("GET", "POST")));

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    if (db.Database.IsSqlite())
    {
        // Local development and tests only. SQL Server uses versioned migrations.
        await db.Database.EnsureCreatedAsync();
    }
    else if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        await db.Database.MigrateAsync();
    }
}

public partial class Program;
