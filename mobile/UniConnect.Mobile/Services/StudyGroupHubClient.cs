using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace UniConnect.Mobile.Services;

/// <summary>
/// The mobile end of /studygroupHub — the same hub the web chat uses, so a
/// message sent from a phone appears in a browser and the other way round.
///
/// One connection is shared by the whole app; pages subscribe to the hub groups
/// they care about and unsubscribe when they leave. Reconnects re-join those
/// groups, because SignalR group membership is per connection and is lost when
/// the transport drops.
/// </summary>
public class StudyGroupHubClient : IAsyncDisposable
{
    private readonly SessionStore _session;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;

    /// <summary>
    /// Hub-group subscriptions, counted rather than flagged. The details page
    /// and the chat page both watch group-{id}, so a plain set would let
    /// whichever closed first unsubscribe the other. Also the list of what to
    /// re-join after a reconnect.
    /// </summary>
    private readonly Dictionary<int, int> _joinedGroups = new();
    private bool _inLobby;

    public StudyGroupHubClient(SessionStore session) => _session = session;

    /// <summary>A message was posted to a group this connection has joined.</summary>
    public event Action<LiveMessage>? MessageReceived;

    /// <summary>Membership or status changed on a joined group; re-read it.</summary>
    public event Action? GroupUpdated;

    /// <summary>A group was created, filled, or changed status; the browse list is stale.</summary>
    public event Action? ListChanged;

    /// <summary>Connection state, for the "connecting… / live" indicator.</summary>
    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    // ---- connection -------------------------------------------------------

    /// <summary>
    /// Connects if needed. Returns false rather than throwing when the hub is
    /// unreachable: live updates are an enhancement, and the REST calls that
    /// actually load the screen have their own error handling.
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is { State: HubConnectionState.Connected }) return true;

            _connection ??= Build();

            if (_connection.State == HubConnectionState.Disconnected)
            {
                StateChanged?.Invoke(HubConnectionState.Connecting);
                await _connection.StartAsync();
                StateChanged?.Invoke(_connection.State);
            }

            return _connection.State == HubConnectionState.Connected;
        }
        catch (Exception)
        {
            // Unreachable server, expired token, transport blocked — the app
            // stays usable without live updates.
            StateChanged?.Invoke(HubConnectionState.Disconnected);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private HubConnection Build()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(ApiConfig.BaseAddress, "studygroupHub"), options =>
            {
                // SignalR puts this in the access_token query string for
                // WebSockets and in the Authorization header otherwise. The
                // server accepts both: Program.cs reads access_token for hub
                // paths specifically because a WebSocket cannot set headers.
                options.AccessTokenProvider = async () => await _session.GetTokenAsync();

                options.HttpMessageHandlerFactory = ApiHttp.ApplyDevCertificate;

                var check = ApiHttp.DevCertificateCheck;
                if (check is not null)
                {
                    // The WebSocket transport does its own TLS handshake and
                    // does not go through the message handler above.
                    options.WebSocketConfiguration = socket =>
                        socket.RemoteCertificateValidationCallback =
                            (_, certificate, chain, errors) =>
                                check(null, certificate as System.Security.Cryptography.X509Certificates.X509Certificate2, chain, errors);
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
            .WithAutomaticReconnect()
            .Build();

        connection.On<LiveMessage>("ReceiveMessage", message => MessageReceived?.Invoke(message));
        connection.On("GroupUpdated", () => GroupUpdated?.Invoke());
        connection.On("StudyGroupListChanged", () => ListChanged?.Invoke());

        connection.Reconnecting += _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Connected);

            // A reconnect is a new connection id, so every group has to be
            // re-joined or this client silently stops receiving anything.
            if (_inLobby) await InvokeAsync("JoinStudyGroupsLobby");

            foreach (var groupId in _joinedGroups.Keys.ToList())
                await InvokeAsync("JoinGroup", groupId);
        };

        connection.Closed += _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Disconnected);
            return Task.CompletedTask;
        };

        return connection;
    }

    // ---- membership -------------------------------------------------------

    public async Task JoinGroupAsync(int groupId)
    {
        var alreadyJoined = _joinedGroups.TryGetValue(groupId, out var count);
        _joinedGroups[groupId] = count + 1;

        if (!await ConnectAsync()) return;

        // Re-invoking is harmless, but only the first subscriber needs to.
        if (!alreadyJoined) await InvokeAsync("JoinGroup", groupId);
    }

    public async Task LeaveGroupAsync(int groupId)
    {
        if (!_joinedGroups.TryGetValue(groupId, out var count)) return;

        if (count > 1)
        {
            // Another page is still watching this group.
            _joinedGroups[groupId] = count - 1;
            return;
        }

        _joinedGroups.Remove(groupId);
        await InvokeAsync("LeaveGroup", groupId);
    }

    public async Task JoinLobbyAsync()
    {
        _inLobby = true;
        if (await ConnectAsync()) await InvokeAsync("JoinStudyGroupsLobby");
    }

    public async Task LeaveLobbyAsync()
    {
        _inLobby = false;
        await InvokeAsync("LeaveStudyGroupsLobby");
    }

    private async Task InvokeAsync(string method, params object?[] args)
    {
        if (_connection is not { State: HubConnectionState.Connected }) return;

        try
        {
            await _connection.InvokeCoreAsync(method, args);
        }
        catch (Exception)
        {
            // A dropped connection mid-call is what WithAutomaticReconnect and
            // the re-join above exist to repair.
        }
    }

    /// <summary>Drops the connection — used on sign-out so the next student starts clean.</summary>
    public async Task StopAsync()
    {
        _joinedGroups.Clear();
        _inLobby = false;

        if (_connection is null) return;

        try
        {
            await _connection.StopAsync();
        }
        catch (Exception)
        {
            // Nothing useful to do; the connection is going away regardless.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The ReceiveMessage payload broadcast by StudyGroupService.PostMessageAsync.
/// sentAtUtc exists specifically for native clients — sentAt is a
/// server-formatted, culture-dependent display string kept for the web's JS.
/// </summary>
public class LiveMessage
{
    public int MessageId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }

    public MessageDto ToMessage(string? myUserId) => new()
    {
        Id = MessageId,
        SenderId = SenderId,
        SenderName = SenderName,
        Content = Content,
        SentAtUtc = SentAtUtc,
        IsMine = SenderId == myUserId
    };
}
