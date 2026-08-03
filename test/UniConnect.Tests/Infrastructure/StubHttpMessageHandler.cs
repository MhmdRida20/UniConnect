using System.Net;
using System.Text;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Canned HTTP responses for RealApiUniversityProvider, which calls each
/// university's registrar API through the named "UniversityApi" client.
///
/// Lets the tests cover what a real integration actually does on a bad day —
/// 404 for an unknown student, 503 when the partner is down, malformed JSON —
/// without a network or a live server.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpResponseMessage> Respond)> _rules = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Response used when no rule matches.</summary>
    public HttpStatusCode DefaultStatus { get; set; } = HttpStatusCode.NotFound;

    public StubHttpMessageHandler RespondTo(string pathContains, HttpStatusCode status, string? json = null)
    {
        _rules.Add((
            req => req.RequestUri?.AbsoluteUri.Contains(pathContains, StringComparison.OrdinalIgnoreCase) == true,
            () => new HttpResponseMessage(status)
            {
                Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
            }));
        return this;
    }

    public StubHttpMessageHandler AlwaysThrow(Exception exception)
    {
        _rules.Insert(0, (_ => true, () => throw exception));
        return this;
    }

    public HttpClient CreateClient(string baseAddress = "https://registrar.test/")
        => new(this) { BaseAddress = new Uri(baseAddress) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var rule = _rules.FirstOrDefault(r => r.Match(request));
        if (rule.Respond is not null) return Task.FromResult(rule.Respond());

        return Task.FromResult(new HttpResponseMessage(DefaultStatus)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });
    }
}
