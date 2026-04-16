using MicroClaw.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

public sealed class MicroEngineHostedService(
    MicroEngine microEngine,
    ILogger<MicroEngineHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly MicroEngine _microEngine = microEngine;
    private readonly ILogger<MicroEngineHostedService> _logger = logger;
    private readonly CancellationTokenSource _stopLoopSignal = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _microEngine.StartAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource executionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _stopLoopSignal.Token);
        CancellationToken executionToken = executionCts.Token;

        try
        {
            await _microEngine.RunAsync(executionToken);
        }
        catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MicroEngine run loop failed.");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Exception? baseStopException = null;
        Exception? engineStopException = null;

        if (!_stopLoopSignal.IsCancellationRequested)
            await _stopLoopSignal.CancelAsync();

        try
        {
            await base.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            baseStopException = ex;
        }

        try
        {
            using CancellationTokenSource engineStopCts = new();
            using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
            {
                ((CancellationTokenSource)state!).CancelAfter(StopTimeout);
            }, engineStopCts);

            if (cancellationToken.IsCancellationRequested)
                engineStopCts.CancelAfter(StopTimeout);

            CancellationToken engineStopToken = engineStopCts.Token;
            await _microEngine.StopAsync(engineStopToken);
        }
        catch (Exception ex)
        {
            engineStopException = ex;
        }

        if (baseStopException is not null && engineStopException is not null)
            throw new AggregateException(baseStopException, engineStopException);

        if (engineStopException is not null)
            throw engineStopException;

        if (baseStopException is not null)
            throw baseStopException;
    }

    public override void Dispose()
    {
        _stopLoopSignal.Dispose();
        base.Dispose();
    }
}