namespace UniConnect.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    private bool _handled;

    public ScanPage()
    {
        InitializeComponent();
    }

    private async void OnBarcodesDetected(object? sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        if (_handled) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;

        _handled = true;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var token = ExtractToken(value);
            await Shell.Current.GoToAsync($"..?scanned={Uri.EscapeDataString(token)}");
        });
    }

    /// <summary>
    /// The QR the web app generates encodes a full check-in URL
    /// ("…/Attendance/ScanSubmit?token=XYZ"), not a bare token. Pulls just the
    /// token back out; falls back to the raw scanned value if it is not a URL,
    /// so a bare token (typed into a test QR generator, say) still works.
    /// </summary>
    private static string ExtractToken(string scannedValue)
    {
        if (Uri.TryCreate(scannedValue, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "token")
                    return Uri.UnescapeDataString(kv[1]);
            }
        }
        return scannedValue.Trim();
    }

    private async void OnCancelTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");
}
