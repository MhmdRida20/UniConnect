// Areas/Identity/Pages/Account/ConfirmEmail.cshtml.cs
//
// Confirmation by typed code rather than clicked link.
//
// The scaffolded version confirmed on GET, reading a Base64Url-encoded Data
// Protection token out of the query string. That token is unguessable but
// untypeable; the Email token provider (wired in Program.cs) emits six digits
// instead, which the user can read off their phone and type here.
//
// The trade is deliberate and has one condition attached. Six digits is a
// million possibilities living 6-9 minutes, and ConfirmEmailAsync counts
// nothing on its own - so without the attempt limiting below, this page would
// be a genuine downgrade from the link it replaces, not an improvement.

#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UniConnect.Models;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ConfirmEmailModel> _logger;

        public ConfirmEmailModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<ConfirmEmailModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>Shown on the page so the user can see which inbox to check.</summary>
        public string Email { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Enter the 6-digit code from your email.")]
            [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
            [Display(Name = "Confirmation code")]
            public string Code { get; set; }

            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return RedirectToPage("./Login");

            Email = email;
            Input = new InputModel { Email = email };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Email = Input?.Email;

            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);

            // Deliberately the same outcome as a wrong code. Saying "no such
            // account" here would turn this page into a way to test which email
            // addresses are registered.
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "That code is not valid. Check it, or request a new one.");
                return Page();
            }

            if (user.EmailConfirmed)
            {
                StatusMessage = "Your email is already confirmed — you can sign in.";
                return RedirectToPage("./Login");
            }

            // Attempt limiting. ConfirmEmailAsync applies no lockout of its own,
            // so without this a six-digit code could simply be guessed at within
            // its window. Reuses the same lockout settings as password sign-in.
            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty,
                    "Too many incorrect codes. Please wait a few minutes and request a new one.");
                return Page();
            }

            var result = await _userManager.ConfirmEmailAsync(user, Input.Code);

            if (!result.Succeeded)
            {
                await _userManager.AccessFailedAsync(user);
                _logger.LogWarning("Failed email confirmation attempt for {Email}.", Input.Email);

                ModelState.AddModelError(string.Empty,
                    "That code is not valid or has expired. Check it, or request a new one.");
                return Page();
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            // The code stays valid for the rest of its window otherwise -
            // ConfirmEmailAsync does not touch the security stamp. Rotating it
            // makes the code single-use. Safe here: the user is not signed in
            // yet, so there is no session to disturb.
            await _userManager.UpdateSecurityStampAsync(user);

            _logger.LogInformation("Email confirmed for {Email}.", Input.Email);

            StatusMessage = "Thank you — your email is confirmed. You can now sign in.";
            return RedirectToPage("./Login");
        }

        /// <summary>Issues a fresh code without leaving the page.</summary>
        public async Task<IActionResult> OnPostResendAsync()
        {
            Email = Input?.Email;

            if (string.IsNullOrWhiteSpace(Input?.Email))
            {
                ModelState.AddModelError(string.Empty, "Enter your email address first.");
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);

            // Same wording whether or not the account exists, and whether or not
            // it is already confirmed - for the same reason as above.
            if (user is not null && !user.EmailConfirmed)
            {
                await EmailCodeSender.SendAsync(_userManager, _emailSender, user);
            }

            ModelState.Clear();
            StatusMessage = $"If {Input.Email} needs confirming, a new code is on its way.";
            Input = new InputModel { Email = Input.Email };
            return Page();
        }
    }
}
