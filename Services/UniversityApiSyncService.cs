namespace UniConnect.Services
{
    /// <summary>
    /// The "cron job" piece of the API integration — periodically resolves
    /// a fresh UniversityApiSyncRunner (scoped, needs its own DbContext) and
    /// runs a full test+sync cycle for every "api"-mode university. See
    /// UniversityApiSyncRunner for the actual test/sync/cache logic, and
    /// AdminUniversitiesController for the manual "Sync Now" button that
    /// calls the exact same runner on demand.
    /// </summary>
    public class UniversityApiSyncService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<UniversityApiSyncService> _logger;
        private readonly TimeSpan _checkInterval;

        public UniversityApiSyncService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<UniversityApiSyncService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("UniversityApi:SyncIntervalMinutes") ?? 10;
            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<UniversityApiSyncRunner>();
                    await runner.SyncAllApiUniversitiesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while running the university API sync cycle.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}
