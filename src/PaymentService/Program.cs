using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PaymentService.Api;
using PaymentService.Application;
using PaymentService.Clients;
using PaymentService.Domain.Metodos;
using PaymentService.Infrastructure;
using PaymentService.Recovery;
using PaymentService.Security;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<PaymentDbContext>(o => o.UseNpgsql(config.GetConnectionString("Payments")));

builder.Services.AddPayliteAuth(config);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Strategy + Factory: cada método se registra una vez; la factory recibe IEnumerable<IMetodoPago>.
builder.Services.AddSingleton<IMetodoPago, Tarjeta>();
builder.Services.AddSingleton<IMetodoPago, Transferencia>();
builder.Services.AddSingleton<IMetodoPago, Billetera>();
builder.Services.AddSingleton<MetodoPagoFactory>();

builder.Services.AddScoped<PagoService>();
builder.Services.AddSingleton(TimeProvider.System);

// AccountService: timeout por intento, reintentos con backoff y circuit breaker.
var cuentas = config.GetSection("AccountService");
builder.Services
    .AddHttpClient<AccountClient>(c => c.BaseAddress = new Uri(cuentas["BaseUrl"] ?? "http://localhost:5001/"))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = cuentas.GetValue("AttemptTimeout", TimeSpan.FromSeconds(2));
        o.TotalRequestTimeout.Timeout = cuentas.GetValue("TotalTimeout", TimeSpan.FromSeconds(6));
        o.Retry.MaxRetryAttempts = cuentas.GetValue("MaxRetries", 2);
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);
        // El circuit breaker exige una ventana de muestreo ≥ 2 × timeout por intento.
        o.CircuitBreaker.SamplingDuration = o.AttemptTimeout.Timeout * 5;
    });

builder.Services.AddHttpClient(ServiceTokenProvider.NombreCliente, c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();

builder.Services.Configure<RecuperacionOptions>(config.GetSection("Recuperacion"));
builder.Services.AddSingleton<RecuperacionPagosService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RecuperacionPagosService>());

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
