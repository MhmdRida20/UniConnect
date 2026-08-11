using UniConnect.Mobile.Pages;

namespace UniConnect.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Detail pages are pushed onto the current stack rather than being
		// sections of their own, so they are registered as routes instead of
		// appearing in the XAML above.
		Routing.RegisterRoute(nameof(GroupDetailsPage), typeof(GroupDetailsPage));
		Routing.RegisterRoute(nameof(CreateGroupPage), typeof(CreateGroupPage));
		Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
	}
}
