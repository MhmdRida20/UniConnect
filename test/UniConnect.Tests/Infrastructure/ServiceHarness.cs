using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UniConnect.Data;
using UniConnect.Services;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Real instances of the two cross-cutting services that aren't behind an
/// interface.
///
/// Using the genuine classes rather than fakes is a deliberate choice here:
/// both do nothing but write rows, so a test can assert on the audit trail and
/// the notifications that were actually produced — which is a stronger claim
/// than "a mock was called".
/// </summary>
public static class ServiceHarness
{
    public static AuditLogService AuditLog(ApplicationDbContext db) =>
        new(db, NullLogger<AuditLogService>.Instance, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        });

    public static NotificationService Notifications(
        ApplicationDbContext db, StubHubContext<UniConnect.Hubs.NotificationHub>? hub = null) =>
        new(db, hub ?? new StubHubContext<UniConnect.Hubs.NotificationHub>());
}
