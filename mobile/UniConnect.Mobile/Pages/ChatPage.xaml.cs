using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

[QueryProperty(nameof(GroupIdParameter), "id")]
public partial class ChatPage : ContentPage
{
	private const int PageSize = 30;

	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;
	private readonly StudyGroupHubClient _hub;
	private readonly ObservableCollection<MessageDto> _messages = new();

	private int _groupId;
	private string? _myUserId;
	private bool _loaded;
	private bool _subscribed;

	public string GroupIdParameter
	{
		set => _groupId = int.TryParse(value, out var parsed) ? parsed : 0;
	}

	public ChatPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();
		_hub = ServiceHelper.Get<StudyGroupHubClient>();

		MessagesView.ItemsSource = _messages;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_myUserId ??= (await _session.GetAsync())?.UserId;

		if (!_loaded) await LoadLatestAsync();

		// Reaching this page requires CanPost from the details screen, so this
		// should always be a member — but if REST already refused the history
		// (membership changed between opening details and opening chat), the
		// hub would refuse the subscription too. No point trying.
		if (_loaded) await ConnectLiveAsync();
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();

		if (!_subscribed) return;
		_subscribed = false;

		_hub.MessageReceived -= OnLiveMessage;
		_hub.StateChanged -= OnHubStateChanged;

		// The connection stays up for the rest of the app; only this group's
		// subscription goes away.
		await _hub.LeaveGroupAsync(_groupId);
	}

	// ---- live updates -----------------------------------------------------

	private async Task ConnectLiveAsync()
	{
		if (_subscribed) return;
		_subscribed = true;

		_hub.MessageReceived += OnLiveMessage;
		_hub.StateChanged += OnHubStateChanged;

		SetLiveStatus(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connecting);
		await _hub.JoinGroupAsync(_groupId);
		SetLiveStatus(_hub.State);
	}

	private void OnLiveMessage(LiveMessage live)
	{
		// Hub callbacks arrive off the UI thread.
		Dispatcher.Dispatch(() =>
		{
			// The sender already added it locally from the POST response, and a
			// reconnect can replay one — match on the message id so neither
			// shows twice.
			if (_messages.Any(m => m.Id == live.MessageId)) return;

			_messages.Add(live.ToMessage(_myUserId));
			ScrollToEnd();
		});
	}

	private void OnHubStateChanged(Microsoft.AspNetCore.SignalR.Client.HubConnectionState state) =>
		Dispatcher.Dispatch(() => SetLiveStatus(state));

	private void SetLiveStatus(Microsoft.AspNetCore.SignalR.Client.HubConnectionState state)
	{
		LiveStatusLabel.Text = state switch
		{
			Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected => "Live",
			Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connecting => "Connecting…",
			Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Reconnecting => "Reconnecting…",
			_ => "Offline — reopen to refresh"
		};

		// A coloured dot reads faster than the sentence beside it.
		LiveDot.Fill = new SolidColorBrush(state switch
		{
			Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected => Color.FromArgb("#16a34a"),
			Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected => Color.FromArgb("#dc2626"),
			_ => Color.FromArgb("#d97706")
		});
	}

	private async Task LoadLatestAsync()
	{
		SetBusy(true);
		try
		{
			var page = await _api.GetMessagesAsync(_groupId, take: PageSize);

			_messages.Clear();
			foreach (var message in page)
				_messages.Add(Tag(message));

			// A full page back means there is probably more behind it.
			LoadOlderBtn.IsVisible = page.Count >= PageSize;

			_loaded = true;
			ScrollToEnd();
		}
		catch (ApiException ex)
		{
			await ReportAsync(ex);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async void OnLoadOlderClicked(object? sender, EventArgs e)
	{
		if (_messages.Count == 0) return;

		LoadOlderBtn.IsEnabled = false;
		try
		{
			// The oldest message on screen is the cursor; the server returns
			// what came before it.
			var oldest = _messages[0];
			var older = await _api.GetMessagesAsync(_groupId, before: oldest.Id, take: PageSize);

			// Inserted in order at the top, so the reading position stays put.
			for (var i = older.Count - 1; i >= 0; i--)
				_messages.Insert(0, Tag(older[i]));

			LoadOlderBtn.IsVisible = older.Count >= PageSize;
		}
		catch (ApiException ex)
		{
			await ReportAsync(ex);
		}
		finally
		{
			LoadOlderBtn.IsEnabled = true;
		}
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		var content = MessageEntry.Text?.Trim();
		if (string.IsNullOrEmpty(content)) return;

		SendBtn.IsEnabled = false;
		ErrorLabel.IsVisible = false;

		try
		{
			var posted = await _api.PostMessageAsync(_groupId, content);

			// Clear only once the server has accepted it, so a failure doesn't
			// lose what the student typed.
			MessageEntry.Text = string.Empty;

			_messages.Add(Tag(posted));
			ScrollToEnd();
		}
		catch (ApiException ex)
		{
			await ReportAsync(ex);
		}
		finally
		{
			SendBtn.IsEnabled = true;
		}
	}

	/// <summary>Marks authorship, which only the client knows.</summary>
	private MessageDto Tag(MessageDto message)
	{
		message.IsMine = message.SenderId == _myUserId;
		return message;
	}

	private void ScrollToEnd()
	{
		if (_messages.Count == 0) return;
		MessagesView.ScrollTo(_messages.Count - 1, animate: false);
	}

	private async Task ReportAsync(ApiException ex)
	{
		if (ex.IsAuthFailure)
		{
			await _session.ClearAsync();
			await Shell.Current.GoToAsync("//login");
			return;
		}

		ErrorLabel.Text = ex.Message;
		ErrorLabel.IsVisible = true;
	}

	private async void OnBackClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync("..");

	private void SetBusy(bool busy)
	{
		Busy.IsRunning = busy;
		Busy.IsVisible = busy;
	}
}
