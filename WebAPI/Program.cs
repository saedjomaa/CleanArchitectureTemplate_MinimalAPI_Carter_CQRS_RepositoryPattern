using Application;
using Carter;
using Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using WebAPI.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Carter finds every ICarterModule in this assembly (WebAPI only; Application validators stay with the decorators).
builder.Services.AddCarter(new DependencyContextAssemblyCatalog(typeof(Program).Assembly));

// Binding failures (invalid JSON, missing body, wrong value type) throw BadHttpRequestException in every
// environment, so GlobalExceptionHandler turns them into a uniform 400 ProblemDetails instead of an empty 400.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("School")
    ?? throw new InvalidOperationException("The School connection string is required."),
    builder.Configuration.GetValue<bool>("Database:EnableSensitiveDataLogging"));

var app = builder.Build();
await app.Services.MigrateDatabaseAsync(app.Lifetime.ApplicationStopping);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               // OpenAPI document: /openapi/v1.json
    app.MapScalarApiReference();    // Interactive UI that reads it: /scalar
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapHealthChecks("/health");

// All Carter modules go through one group so the ProblemDetails logging filter applies to every endpoint.
app.MapGroup(string.Empty)
    .AddEndpointFilter<ProblemDetailsLoggingFilter>()
    .MapCarter();

app.Run();
