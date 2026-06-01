using System.Diagnostics;
using MediatR;

namespace Avomos.Api.Pipelines;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        logger.LogInformation(
            "[{Request}] handled in {ElapsedMs}ms",
            typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
