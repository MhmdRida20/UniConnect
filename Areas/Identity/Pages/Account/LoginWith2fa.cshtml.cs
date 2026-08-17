// Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs
//
// Scaffolded from the Identity UI package, then adapted to carry the rules the
// password login already enforces. Three differences from the stock page:
//
//   1. A suspended account cannot complete the challenge. SuspendedUserMiddleware
//      would sign them out on their next request anyway, but failing here is
//      honest — it tells them why instead of bouncing them mid-session.
//   2. Successes and failures are written to the audit log, so the trail does
//      not go quiet for exactly the users who enabled the stronger login.
//   3. A missing two-factor user redirects to Login instead of throwing. The
//      stock page throws InvalidOperationException, which renders a 500 for the
//      entirely ordinary case of a bookmarked or expired challenge URL.

#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly AuditLogService _auditLog;

        public LoginWith2faModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginWith2faModel> logger,
            AuditLogService auditLog)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _auditLog = auditLog;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            [Display(Name = "Authenticator code")]
            public string TwoFactorCode { get; set; }

            [Display(Name = "Remember this browser")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
        {
            // Half-authenticated at this point: the password was accepted and a
            // TwoFactorUserId cookie issued, but no identity cookie exists yet.
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null) return RedirectToPage("./Login", new { ReturnUrl = returnUrl });

            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null) return RedirectToPage("./Login", new { ReturnUrl = returnUrl });

            if (!ModelState.IsValid)
            {
                ReturnUrl = returnUrl;
                RememberMe = rememberMe;
                return Page();
            }

            // Checked before the code is even verified: a suspended account
            // should not be told whether its code was right.
            if (user.IsSuspended)
            {
                await _signInManager.SignOutAsync();
                await _auditLog.LogAsync(
                    "FailedLogin",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "Two-factor challenge refused: account is suspended.");

                ModelState.AddModelError(string.Empty, "This account has been suspended.");
                ReturnUrl = returnUrl;
                RememberMe = rememberMe;
                return Page();
            }

            // Authenticator apps group digits for readability; users paste what
            // they see, so strip the separators rather than reject the code.
            var authenticatorCode = Input.TwoFactorCode
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);

            // lockoutOnFailure is implicit here and matches Login.cshtml.cs:92 —
            // repeated wrong codes lock the account exactly as wrong passwords do.
            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                authenticatorCode, rememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
                await _auditLog.LogAsync(
                    "TwoFactorLoginSucceeded",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "Signed in with an authenticator code.");

                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
                await _auditLog.LogAsync(
                    "FailedLogin",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "Locked out after repeated invalid authenticator codes.");

                return RedirectToPage("./Lockout");
            }

            _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
            await _auditLog.LogAsync(
                "FailedLogin",
                userId: user.Id, universityCode: user.UniversityCode,
                entityType: "User", entityId: user.Id,
                details: "Invalid authenticator code at the two-factor challenge.");

            ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
            ReturnUrl = returnUrl;
            RememberMe = rememberMe;
            return Page();
        }
    }
}
