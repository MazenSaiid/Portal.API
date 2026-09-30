using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Application.Features.Sla;

namespace Portal.Infrastructure.Sla;

public sealed class SlaMonitorOptions
{
    public const string SectionName = "Sla";
    public bool MonitorEnabled { get; init; } = true;
    public int EvaluationIntervalSeconds { get; init; } = 60;
}

/// <summary>Runs the escalation rules on a timer (Spec 007, S8). A failed run is logged and retried next tick.</summary>
public sealed class SlaMonitor(IServiceScopeFactory scopes, IOptions<SlaMonitorOptions> options, TimeProvider clock, ILogger<SlaMonitor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.MonitorEnabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.EvaluationIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISlaEngine>().RunAsync(clock.GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SLA evaluation failed; will retry on the next tick");
            }
        }
    }
}
