using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Adapters
{
    /// <summary>
    /// Picks which IUniversityProvider implementation actually talks to a
    /// given university's API. Originally every university used the same
    /// implementation (RealApiUniversityProvider, matching the built-in
    /// simulator's shape); this is where a genuinely different real
    /// partner's API shape (see UmsApiUniversityProvider) gets routed to
    /// its own implementation instead, based on University.ApiStyle.
    /// </summary>
    public interface IUniversityProviderResolver
    {
        Task<IUniversityProvider> GetProviderAsync(string universityCode);
    }

    public class UniversityProviderResolver : IUniversityProviderResolver
    {
        private readonly RealApiUniversityProvider _simulatedProvider;
        private readonly UmsApiUniversityProvider _umsProvider;
        private readonly ApplicationDbContext _db;

        public UniversityProviderResolver(
            RealApiUniversityProvider simulatedProvider,
            UmsApiUniversityProvider umsProvider,
            ApplicationDbContext db)
        {
            _simulatedProvider = simulatedProvider;
            _umsProvider = umsProvider;
            _db = db;
        }

        public async Task<IUniversityProvider> GetProviderAsync(string universityCode)
        {
            var apiStyle = await _db.Universities
                .Where(u => u.Code == universityCode)
                .Select(u => u.ApiStyle)
                .FirstOrDefaultAsync();

            return apiStyle == "Ums" ? _umsProvider : _simulatedProvider;
        }
    }
}
