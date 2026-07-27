using Microsoft.AspNetCore.Mvc.RazorPages;

namespace UniConnect.Areas.Identity.Pages.Account
{
    /// <summary>
    /// The landing page "Sign Up" actually goes to now — lets someone pick
    /// Student / Instructor / Department Staff before reaching the actual
    /// registration form for that role, instead of expecting them to know
    /// (or be given) a direct URL to the right one.
    /// </summary>
    public class RegisterChoiceModel : PageModel
    {
        public string? ReturnUrl { get; set; }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }
    }
}
