using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// A SignalR IHubContext that records what would have been broadcast instead of
/// sending it. Controllers push real-time updates after every state change, so
/// without this most of them can't be constructed at all.
///
/// Recording rather than discarding: "did submitting attendance notify the
/// instructor's live roster?" is a real behaviour worth asserting, and it's
/// cheap to keep.
/// </summary>
public sealed class StubHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public sealed record Sent(string Target, string Method, object?[] Args);

    private readonly List<Sent> _sent = new();

    public IReadOnlyList<Sent> Messages => _sent;

    public IHubClients Clients { get; }
    public IGroupManager Groups { get; } = new StubGroupManager();

    public StubHubContext() => Clients = new StubClients(_sent);

    public bool SentTo(string target, string method) =>
        _sent.Any(m => m.Target == target && m.Method == method);

    public int CountTo(string target) => _sent.Count(m => m.Target == target);

    // ---- plumbing ----

    private sealed class StubClients : IHubClients
    {
        private readonly List<Sent> _sink;
        public StubClients(List<Sent> sink) => _sink = sink;

        private IClientProxy Proxy(string target) => new StubClientProxy(target, _sink);

        public IClientProxy All => Proxy("all");
        public IClientProxy AllExcept(IReadOnlyList<string> excluded) => Proxy("all-except");
        public IClientProxy Client(string connectionId) => Proxy($"client:{connectionId}");
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy("clients");
        public IClientProxy Group(string groupName) => Proxy(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy(string.Join(',', groupNames));
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excluded) => Proxy(groupName);
        public IClientProxy User(string userId) => Proxy($"user:{userId}");
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy("users");
    }

    private sealed class StubClientProxy : IClientProxy
    {
        private readonly string _target;
        private readonly List<Sent> _sink;

        public StubClientProxy(string target, List<Sent> sink)
        {
            _target = target;
            _sink = sink;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _sink.Add(new Sent(_target, method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class StubGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
