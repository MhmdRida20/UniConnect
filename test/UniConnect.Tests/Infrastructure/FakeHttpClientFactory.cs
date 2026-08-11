using Microsoft.Extensions.Logging.Abstractions;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// The minimum IHttpClientFactory a test needs: one named client, backed by a
/// StubHttpMessageHandler the test controls directly. Real code asks for
/// "UniversityApi" by name (see UniversityApiSyncRunner, RealApiUniversityProvider);
/// this hands back the same HttpClient every time regardless of the name asked
/// for, since these tests only ever exercise one client at once.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public StubHttpMessageHandler Handler { get; } = new();

    public HttpClient CreateClient(string name) => Handler.CreateClient();
}
