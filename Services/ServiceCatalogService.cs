using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Services
{
    public interface IServiceCatalogService
    {
        /// <summary>Is this service both implemented AND turned on for this university?</summary>
        Task<bool> IsServiceEnabledAsync(string universityCode, string serviceCode);

        /// <summary>All service codes enabled for this university (implemented services only).</summary>
        Task<List<string>> GetEnabledServiceCodesAsync(string universityCode);
    }

    public class ServiceCatalogService : IServiceCatalogService
    {
        private readonly ApplicationDbContext _db;

        public ServiceCatalogService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<bool> IsServiceEnabledAsync(string universityCode, string serviceCode)
        {
            return await _db.UniversityServices
                .Include(us => us.Service)
                .AnyAsync(us => us.UniversityCode == universityCode
                             && us.ServiceCode == serviceCode
                             && us.IsEnabled
                             && us.Service!.IsImplemented);
        }

        public async Task<List<string>> GetEnabledServiceCodesAsync(string universityCode)
        {
            return await _db.UniversityServices
                .Include(us => us.Service)
                .Where(us => us.UniversityCode == universityCode
                          && us.IsEnabled
                          && us.Service!.IsImplemented)
                .Select(us => us.ServiceCode)
                .ToListAsync();
        }
    }
}
