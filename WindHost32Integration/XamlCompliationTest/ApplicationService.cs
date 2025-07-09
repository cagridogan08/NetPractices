

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TESA.Runner.ProjectService;
internal class ApplicationService : IHostedService
{
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(ILogger<ApplicationService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Service is running");
            await Task.Delay(1000, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service is stopping");
        return Task.CompletedTask;
    }
}
