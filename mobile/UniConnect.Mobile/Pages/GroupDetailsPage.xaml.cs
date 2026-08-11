using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

[QueryProperty(nameof(GroupIdParameter), "id")]
public partial class GroupDetailsPage : ContentPage
{
	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;
	private readonly StudyGroupHubClient _hub;

	private int _groupId;
	private GroupDetail? _detail;
	private string? _myUserId;
	private bool? _twoColumn;

	/// <summary>
	/// Whether this page currently holds a live subscription to the group's
	/// hub group. Kept in step with membership rather than "did OnAppearing
	/// run once" — the hub only accepts approved members (StudyGroupHub.
	/// JoinGroup checks and refuses everyone else), so subscribing while
	/// merely browsing a group always fails. Re-evaluated after every load,
	/// so joining the group during this visit picks up live updates instead
	/// of waiting for the next OnAppearing.
	/// </summary>
	private bool _liveSubscribed;

	/// <summary>
	/// Route parameter; Shell always hands these over as strings. Named
	/// GroupIdParameter rather than Id because Element already has an Id.
	/// </summary>
	public string GroupIdParameter
	{
		set => _groupId = int.TryParse(value, out var parsed) ? parsed : 0;
	}

	public GroupDetailsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();
		_hub = ServiceHelper.Get<StudyGroupHubClient>();

		SizeChanged += OnPageSizeChanged;
	}

	// ---- responsive layout -------------------------------------------------

	/// <summary>
	/// The web splits this page into "col-lg-8 / col-lg-4" once there is room.
	/// Same idea here: past the lg breakpoint the members and requests move
	/// beside the detail instead of far below it.
	/// </summary>
	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (Width <= 0) return;

		ContentGrid.MaximumWidthRequest = Responsive.ContentMaxWidth;

		var twoColumn = Responsive.IsTwoColumn(Width);
		if (_twoColumn == twoColumn) return;
		_twoColumn = twoColumn;

		if (twoColumn)
		{
			// Roughly the web's 8/4 split.
			MainColumn.Width = new GridLength(2, GridUnitType.Star);
			SideColumn.Width = new GridLength(1, GridUnitType.Star);

			Grid.SetRow(SideStack, 0);
			Grid.SetColumn(SideStack, 1);
		}
		else
		{
			MainColumn.Width = new GridLength(1, GridUnitType.Star);
			SideColumn.Width = new GridLength(0);

			Grid.SetRow(SideStack, 1);
			Grid.SetColumn(SideStack, 0);
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_myUserId ??= (await _session.GetAsync())?.UserId;
		await LoadAsync();
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();

		// Safe even though the chat page pushed on top joins the same hub
		// group: the client counts subscribers, so this only detaches this
		// page's interest. OnAppearing re-joins on the way back.
		if (!_liveSubscribed) return;
		_liveSubscribed = false;

		_hub.GroupUpdated -= OnGroupUpdated;
		await _hub.LeaveGroupAsync(_groupId);
	}

	/// <summary>
	/// Joins or leaves the hub group so the subscription matches whether the
	/// server would actually accept it — an approval, a leave, or a join that
	/// happened during this visit all change the answer.
	/// </summary>
	private async Task SyncLiveSubscriptionAsync(bool eligible)
	{
		if (eligible && !_liveSubscribed)
		{
			_liveSubscribed = true;
			_hub.GroupUpdated += OnGroupUpdated;
			await _hub.JoinGroupAsync(_groupId);
		}
		else if (!eligible && _liveSubscribed)
		{
			_liveSubscribed = false;
			_hub.GroupUpdated -= OnGroupUpdated;
			await _hub.LeaveGroupAsync(_groupId);
		}
	}

	/// <summary>
	/// Re-reads rather than patching: the payload says only that something
	/// changed, and the permission flags on this screen are the server's to
	/// decide.
	/// </summary>
	private void OnGroupUpdated() => Dispatcher.Dispatch(async () => await LoadAsync());

	// ---- loading and rendering --------------------------------------------

	private async Task LoadAsync()
	{
		SetBusy(true);
		LoadErrorLabel.IsVisible = false;

		try
		{
			_detail = await _api.GetGroupAsync(_groupId);
			Render(_detail);
			Body.IsVisible = true;

			await SyncLiveSubscriptionAsync(_detail.AmMember);
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			Body.IsVisible = false;
			LoadErrorLabel.Text = ex.Message;
			LoadErrorLabel.IsVisible = true;

			// The group is gone, or access was refused — either way, nothing
			// left here to hold a subscription open for.
			await SyncLiveSubscriptionAsync(eligible: false);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void Render(GroupDetail detail)
	{
		Title = detail.GroupName;

		CoursePill.Text = detail.CourseLine;
		NameLabel.Text = detail.GroupName;
		CreatorLabel.Text = detail.CreatorLabel;
		CreatedLabel.Text =
			DateTime.SpecifyKind(detail.CreatedAt, DateTimeKind.Utc).ToLocalTime().ToString("MMM dd, yyyy");

		InactiveNotice.IsVisible = detail.IsInactive;
		InactiveNoticeLabel.Text = detail.AmMember
			? "This group has been marked inactive due to no recent activity. Send a message or approve a member to bring it back."
			: "This group has been marked inactive due to no recent activity.";

		StatMembers.Text = detail.MembersLine;
		StatLocation.Text = detail.MeetingLocationOrPlaceholder;
		StatStatus.Text = detail.Status;

		AboutLabel.Text = detail.DescriptionOrPlaceholder;
		MinMembersLabel.Text = $"Minimum to run: {detail.MinMembers} members";

		RenderMembership(detail);
		RenderMembers(detail);
	}

	/// <summary>
	/// The action area. CanJoin comes from the server's own rules — capacity,
	/// status, existing membership — so nothing here re-decides who may join.
	/// </summary>
	private void RenderMembership(GroupDetail detail)
	{
		ErrorLabel.IsVisible = false;

		// Membership state, said plainly — "Pending" on its own reads as a
		// property of the group rather than of the student's request.
		MembershipLabel.Text = detail switch
		{
			{ AmCreator: true } => "You created this group.",
			{ MyStatus: "Approved" } => "You are a member of this group.",
			{ MyStatus: "Pending" } => "Your request to join is waiting for approval.",
			{ MyStatus: "Rejected" } => "Your request to join was declined.",
			_ => string.Empty
		};
		MembershipRow.IsVisible = MembershipLabel.Text.Length > 0;

		MembershipIcon.Source = detail switch
		{
			{ MyStatus: "Pending" } => "ic_clock_amber.png",
			{ MyStatus: "Rejected" } => "ic_close_red.png",
			_ => "ic_check_circle_green.png"
		};

		// Web parity: a pending request offers "Withdraw request" rather than
		// "Leave", which is the same endpoint said in the words that fit.
		WithdrawBtn.IsVisible = detail.AmPending;

		if (detail.CanJoin)
		{
			ActionBtn.Text = detail.IsFull ? "Group full" : "Request to Join";
			ActionBtn.IsEnabled = !detail.IsFull;
			ActionBtn.Style = null;
			ActionBtn.IsVisible = true;
		}
		else if (detail.CanLeave)
		{
			ActionBtn.Text = "Leave Group";
			ActionBtn.IsEnabled = true;
			ActionBtn.Style = AppStyle("UcBtnDanger");
			ActionBtn.IsVisible = true;
		}
		else
		{
			ActionBtn.IsVisible = false;
		}

		// CanPost is the server's answer to "may this person use the chat".
		ChatBtn.IsVisible = detail.CanPost;
		ChatLockedRow.IsVisible = !detail.CanPost;

		// Only the creator may delete, and only while it still exists.
		DeleteBtn.IsVisible = detail.AmCreator && !detail.IsArchived;
	}

	private void RenderMembers(GroupDetail detail)
	{
		foreach (var member in detail.Members)
		{
			member.IsSelf = member.UserId == _myUserId;
			member.IsGroupCreator = member.UserId == detail.CreatorId;

			// The creator may promote or remove anyone but themselves. The
			// server enforces this too; the UI just doesn't offer what would
			// be refused.
			member.ShowManageActions = detail.AmCreator && !member.IsGroupCreator;
		}

		MemberCountLabel.Text = detail.ApprovedCount.ToString();
		BindableLayout.SetItemsSource(MembersLayout, detail.Members);

		// Pending rows only come back from the server for the creator, so this
		// panel simply never populates for anyone else.
		var hasPending = detail.AmCreator && detail.Pending.Count > 0;
		PendingPanel.IsVisible = hasPending;
		PendingCountLabel.Text = detail.Pending.Count.ToString();
		BindableLayout.SetItemsSource(PendingLayout, hasPending ? detail.Pending : null);
	}

	// ---- actions ----------------------------------------------------------

	private async void OnActionClicked(object? sender, EventArgs e)
	{
		if (_detail is null) return;

		if (_detail.CanJoin)
		{
			await RunAsync(() => _api.JoinAsync(_groupId), "Done");
			return;
		}

		var confirmed = await DisplayAlert(
			"Leave group", $"Leave \"{_detail.GroupName}\"?", "Leave", "Cancel");

		if (confirmed) await RunAsync(() => _api.LeaveAsync(_groupId), "Left group");
	}

	private async void OnWithdrawClicked(object? sender, EventArgs e)
	{
		var confirmed = await DisplayAlert(
			"Withdraw request", "Withdraw your request to join this group?", "Withdraw", "Cancel");

		if (confirmed) await RunAsync(() => _api.LeaveAsync(_groupId), "Request withdrawn");
	}

	private async void OnApproveClicked(object? sender, EventArgs e)
	{
		if (MemberIdOf(sender) is not int memberId) return;
		await RunAsync(() => _api.ApproveMemberAsync(memberId), "Approved");
	}

	private async void OnRejectClicked(object? sender, EventArgs e)
	{
		if (MemberIdOf(sender) is not int memberId) return;

		var confirmed = await DisplayAlert(
			"Reject request", "Reject this student's request to join?", "Reject", "Cancel");

		if (confirmed) await RunAsync(() => _api.RejectMemberAsync(memberId), "Rejected");
	}

	private async void OnRemoveClicked(object? sender, EventArgs e)
	{
		if (MemberIdOf(sender) is not int memberId) return;

		var confirmed = await DisplayAlert(
			"Remove member", "Remove this member from the group?", "Remove", "Cancel");

		if (confirmed) await RunAsync(() => _api.RemoveMemberAsync(memberId), "Removed");
	}

	private async void OnTransferClicked(object? sender, EventArgs e)
	{
		if (MemberIdOf(sender) is not int memberId) return;

		var confirmed = await DisplayAlert(
			"Transfer leadership",
			"Make this member the new group leader? You will no longer be the creator.",
			"Transfer", "Cancel");

		if (confirmed) await RunAsync(() => _api.TransferLeadershipAsync(memberId), "Leadership transferred");
	}

	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		if (_detail is null) return;

		var confirmed = await DisplayAlert(
			"Delete group",
			$"Delete \"{_detail.GroupName}\"? Members will be notified and it will no longer appear in the group list.",
			"Delete", "Cancel");

		if (!confirmed) return;

		SetBusy(true);
		ErrorLabel.IsVisible = false;

		try
		{
			var message = await _api.DeleteAsync(_groupId);

			// Stop watching a group that no longer exists before navigating,
			// otherwise the subscription outlives the page.
			await SyncLiveSubscriptionAsync(eligible: false);

			await DisplayAlert("Deleted", message, "OK");

			// There is nothing left to show here, so go back to the list rather
			// than re-reading a group that browse now hides.
			await Shell.Current.GoToAsync("..");
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorLabel.IsVisible = true;
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async void OnBackClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync("..");

	private async void OnChatClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync($"{nameof(ChatPage)}?id={_groupId}");

	/// <summary>
	/// Runs an API call, reports the server's own wording, and re-reads the
	/// group so every panel reflects what actually happened.
	/// </summary>
	private async Task RunAsync(Func<Task<string>> call, string successTitle)
	{
		SetBusy(true);
		ErrorLabel.IsVisible = false;

		try
		{
			var message = await call();
			await DisplayAlert(successTitle, message, "OK");
			await LoadAsync();
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorLabel.IsVisible = true;

			// A lost concurrency race means someone else changed the group
			// underneath us; re-reading gives the student an accurate screen.
			if (ex.Code == "CONCURRENCY_RETRY") await LoadAsync();
		}
		finally
		{
			SetBusy(false);
		}
	}

	/// <summary>
	/// Styles live in the merged application dictionaries, not on the page, so
	/// they have to be looked up from there rather than this.Resources.
	/// </summary>
	private static Style? AppStyle(string key) =>
		Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

	private static int? MemberIdOf(object? sender) =>
		(sender as Button)?.CommandParameter is int id ? id : null;

	private void SetBusy(bool busy)
	{
		Busy.IsRunning = busy;
		Busy.IsVisible = busy;
	}

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
