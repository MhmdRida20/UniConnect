using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// One place to record an audit entry. Edge Case: "Audit log failure —
    /// the audit logging service fails temporarily. The system shall retry
    /// logging and alert administrators." — retries once; if it still
    /// fails, logs a Critical-level entry via ILogger (the closest
    /// equivalent to "alert administrators" without a dedicated
    /// ops/alerting stack, which is out of scope for this project) rather
    /// than silently losing the record or crashing the calling action.
    ///
    /// Deliberately never throws — a logging failure should never be the
    /// reason a real user-facing action (submitting attendance, creating a
    /// ticket, etc.) fails.
    /// </summary>
    public class AuditLogService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AuditLogService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditLogService(ApplicationDbContext db, ILogger<AuditLogService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(
            string action, string? userId = null, string? universityCode = null,
            string? entityType = null, string? entityId = null, string? details = null)
        {
            var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            var entry = new AuditLog
            {
                UserId = userId,
                UniversityCode = universityCode,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    _db.AuditLogs.Add(entry);
                    await _db.SaveChangesAsync();
                    return;
                }
                catch (Exception ex) when (attempt == 1)
                {
                    _logger.LogWarning(ex, "Audit log write failed (attempt {Attempt}) for action {Action} — retrying once.", attempt, action);
                }
                catch (Exception ex)
                {
                    // Retry also failed — this is the "alert administrators" step.
                    _logger.LogCritical(ex,
                        "AUDIT LOG FAILURE: could not record action '{Action}' (user {UserId}) after retry. Details: {Details}",
                        action, userId, details);
                }
            }
        }
    }
}
