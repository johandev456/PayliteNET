using System.Text.Json.Serialization;
using AccountService.Api;
using AccountService.Application;
using AccountService.Infrastructure;
using AccountService.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AccountDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Accounts")));

builder.Services.AddPayliteAuth(builder.Configuration);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddScoped<CuentaService>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

var app = builder.Build();

// Dev/compose: aplica migraciones pendientes al arrancar. En producción se aplicaría
// el script SQL de docs/sql desde el pipeline, con esta opción en false.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AccountDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages(); // 401/403/404 sin cuerpo → ProblemDetails
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
