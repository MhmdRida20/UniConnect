// Areas/Identity/Pages/Account/Login.cshtml.cs
//
// This REPLACES the default embedded Login page — needed specifically to
// log failed login attempts (FR-92), since there's no built-in event
// equivalent to CookieAuthenticationEvents.OnSignedIn for "a login was
// attempted and failed." Successful logins are logged separately, in
// Program.cs's OnSignedIn hook, without needing this page at all.
//
// To get this file in your project (if starting from scratch), scaffold
// Identity's Login page the same way Register was scaffolded, then replace
// its contents with the code below.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly AuditLogService _auditLog;

        // FR-03: read here in the code-behind (not via @inject in the .cshtml)
        // deliberately — exposed as a plain bool property so the view just
        // reads @Model.SsoConfigured, no additional Razor directives needed.
        public bool SsoConfigured { get; }

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginModel> logger,
            AuditLogService auditLog,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _auditLog = auditLog;
            SsoConfigured = !string.IsNullOrWhiteSpace(configuration["Oidc:Authority"]);
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();
        public string? ReturnUrl { get; set; }
        [TempData] public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
                ModelState.AddModelError(string.Empty, ErrorMessage);

            returnUrl ??= Url.Content("~/");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid) return Page();

            // The actual sign-in cookie's OnSignedIn event (see Program.cs)
            // logs the SUCCESS case — this method only needs to handle
            // logging the FAILURE case, which has no equivalent built-in hook.
            var result = await _signInManager.PasswordSignInAsync(
                Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }

            var attemptedUser = await _userManager.FindByEmailAsync(Input.Email);
            var reason = result.IsLockedOut ? "LockedOut" : result.IsNotAllowed ? "NotAllowed (e.g. unconfirmed email or suspended)" : "InvalidCredentials";

            await _auditLog.LogAsync(
                "FailedLogin",
                userId: attemptedUser?.Id,
                universityCode: attemptedUser?.UniversityCode,
                entityType: "User",
                entityId: attemptedUser?.Id,
                details: $"Email attempted: {Input.Email}; reason: {reason}");

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }
    }
}
