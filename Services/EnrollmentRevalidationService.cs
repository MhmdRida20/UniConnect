namespace UniConnect.Services
{
    /// <summary>
    /// Periodic trigger for EnrollmentRevalidationRunner — see that class
    /// for the actual re-check-and-remove-and-notify logic, and
    /// AdminExternalApiSimulatorController for the immediate on-demand
    /// trigger used while testing/demoing.
    /// </summary>
    public class EnrollmentRevalidationService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<EnrollmentRevalidationService> _logger;
        private readonly TimeSpan _checkInterval;

        public EnrollmentRevalidationService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<EnrollmentRevalidationService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("StudyGroups:EnrollmentRevalidationIntervalMinutes") ?? 60;
            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<EnrollmentRevalidationRunner>();
                    await runner.RevalidateAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while revalidating study group enrollments.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}
