using Portal.API.Infrastructure;
using Portal.Application;
using Portal.Infrastructure;
using Portal.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// `dotnet run -- --reset-demo` deletes every record and loads the demo data described in docs/demo.md.
if (args.Contains("--reset-demo"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("--reset-demo deletes all data, so it only runs in the Development environment.");
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAsync();
}
else if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.UseExceptionHandler();
app.Use((context, next) =>
{
    // Browsers must never guess a content type (matters for downloaded attachments).
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return next();
});
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Portal API");
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(ApiServiceExtensions.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
