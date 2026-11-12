using System.Globalization;
using ColorMatch.API.Configuration;
using ColorMatch.API.Middleware;
using ColorMatch.Application;
using ColorMatch.Infrastructure;
using ColorMatch.Infrastructure.Persistence;
using ColorMatch.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

// Never depend on the server's OS culture for number/date parsing (e.g. tr-TR uses ',' as decimal separator).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

// uploaded media: /media/{key}
var storage = app.Services.GetRequiredService<LocalFileStorage>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storage.RootPath),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable";
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
});

app.UseAuthentication();
app.UseRateLimiter();   // after authentication: attempt limits are per player
app.UseAuthorization();

app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "ColorMatch API v1"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
app.MapControllers();

if (app.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var migrationDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await migrationDb.Database.MigrateAsync();
    await PostColorBackfill.RunAsync(migrationDb);
    await GameCoreBackfill.RunAsync(migrationDb,
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ColorMatch.Domain.Rules.GameBalance>>().Value,
        scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

app.Run();

public partial class Program;
