// Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs
//
// The way back in when the authenticator device is gone. Adapted from the
// scaffolded page in the same three ways as LoginWith2fa: suspended accounts
// are refused, outcomes are audited, and a missing challenge redirects rather
// than throwing.
//
// Redeeming a code consumes it — RedeemTwoFactorRecoveryCodeAsync removes it
// from the stored set — so the remaining count falling to zero is a real
// lockout risk, which is why the page nags about regenerating.

#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginWithRecoveryCodeModel> _logger;
        private readonly AuditLogService _auditLog;

        public LoginWithRecoveryCodeModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginWithRecoveryCodeModel> logger,
            AuditLogService auditLog)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _auditLog = auditLog;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [BindProperty]
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Recovery code")]
            public string RecoveryCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null) return RedirectToPage("./Login", new { ReturnUrl = returnUrl });

            ReturnUrl = returnUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null) return RedirectToPage("./Login", new { ReturnUrl = returnUrl });

            if (!ModelState.IsValid)
            {
                ReturnUrl = returnUrl;
                return Page();
            }

            if (user.IsSuspended)
            {
                await _signInManager.SignOutAsync();
                await _auditLog.LogAsync(
                    "FailedLogin",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "Recovery-code challenge refused: account is suspended.");

                ModelState.AddModelError(string.Empty, "This account has been suspended.");
                ReturnUrl = returnUrl;
                return Page();
            }

            // Codes are displayed hyphenated for legibility; accept them typed
            // either way.
            var recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);

            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

            if (result.Succeeded)
            {
                var remaining = await _userManager.CountRecoveryCodesAsync(user);

                _logger.LogInformation("User with ID '{UserId}' logged in with a recovery code.", user.Id);
                await _auditLog.LogAsync(
                    "TwoFactorRecoveryCodeUsed",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: $"Signed in with a recovery code. {remaining} code(s) remaining.");

                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
                await _auditLog.LogAsync(
                    "FailedLogin",
                    userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "Locked out after repeated invalid recovery codes.");

                return RedirectToPage("./Lockout");
            }

            _logger.LogWarning("Invalid recovery code entered for user with ID '{UserId}'.", user.Id);
            await _auditLog.LogAsync(
                "FailedLogin",
                userId: user.Id, universityCode: user.UniversityCode,
                entityType: "User", entityId: user.Id,
                details: "Invalid recovery code at the two-factor challenge.");

            ModelState.AddModelError(string.Empty, "Invalid recovery code.");
            ReturnUrl = returnUrl;
            return Page();
        }
    }
}
