// Areas/Identity/Pages/Account/RegisterConfirmation.cshtml.cs
//
// Now a redirect rather than a page.
//
// The scaffolded version was a "check your email" dead end whose only real
// content was a development shortcut link that confirmed the account without
// any email at all. With confirmation done by typed code, the useful next step
// is the code form itself, so registration goes straight there and the user
// never sees an interstitial that asks them to do nothing.

#nullable disable

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UniConnect.Models;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class RegisterConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public RegisterConfirmationModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string email, string returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(email)) return RedirectToPage("./Login");

            var user = await _userManager.FindByEmailAsync(email);
            if (user is null) return RedirectToPage("./Login");

            return RedirectToPage("./ConfirmEmail", new { email });
        }
    }
}
