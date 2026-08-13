using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class MyApplicationsPage : ContentPage
{
	private readonly InternshipsApi _api;
	private readonly SessionStore _session;

	private readonly ObservableCollection<ApplicationSummary> _applications = new();

	public MyApplicationsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<InternshipsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		BindableLayout.SetItemsSource(CardsHost, _applications);
		Column.MaximumWidthRequest = Responsive.FormMaxWidth;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	private async Task LoadAsync()
	{
		Refresher.IsRefreshing = true;
		ErrorBox.IsVisible = false;

		try
		{
			var applications = await _api.GetMyApplicationsAsync();

			_applications.Clear();
			foreach (var application in applications)
				_applications.Add(application);

			EmptyState.IsVisible = _applications.Count == 0;
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorBox.IsVisible = true;
		}
		finally
		{
			Refresher.IsRefreshing = false;
		}
	}

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

	private async void OnWithdrawClicked(object? sender, EventArgs e)
	{
		if (sender is not Element element || element.BindingContext is not ApplicationSummary application) return;

		// Withdrawing cannot be undone — the status is terminal afterwards — so
		// it asks first.
		var confirmed = await DisplayAlert(
			"Withdraw application",
			$"Withdraw your application to \"{application.InternshipTitle}\"? This cannot be undone.",
			"Withdraw",
			"Cancel");

		if (!confirmed) return;

		try
		{
			var message = await _api.WithdrawAsync(application.Id);
			await DisplayAlert("Withdrawn", message, "OK");
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;
			await DisplayAlert("Could not withdraw", ex.Message, "OK");
		}

		// Reload either way: a refusal usually means the employer moved the
		// application on while this screen was open.
		await LoadAsync();
	}

	private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
