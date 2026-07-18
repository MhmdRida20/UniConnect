namespace UniConnect.Adapters
{
    /// <summary>
    /// Every university is served by the same real IUniversityProvider
    /// implementation (RealApiUniversityProvider) — there's no mock/real
    /// branching to resolve anymore. This interface still exists (rather
    /// than callers just injecting RealApiUniversityProvider directly) so
    /// the door stays open for a genuinely different provider implementation
    /// later without touching every controller that reads academic data.
    /// </summary>
    public interface IUniversityProviderResolver
    {
        Task<IUniversityProvider> GetProviderAsync(string universityCode);
    }

    public class UniversityProviderResolver : IUniversityProviderResolver
    {
        private readonly RealApiUniversityProvider _provider;

        public UniversityProviderResolver(RealApiUniversityProvider provider)
        {
            _provider = provider;
        }

        public Task<IUniversityProvider> GetProviderAsync(string universityCode)
            => Task.FromResult<IUniversityProvider>(_provider);
    }
}
