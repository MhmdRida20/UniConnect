// Areas/Identity/Pages/Account/Register.cshtml.cs
//
// This file REPLACES the default Register page that Visual Studio scaffolds.
// To get this file in your project, you must first SCAFFOLD the Identity Register page:
//   - In Solution Explorer, right-click the project → Add → New Scaffolded Item...
//   - Choose "Identity" → Add
//   - Tick "Account/Register" → choose your ApplicationDbContext → Add
// Visual Studio will then create this file under Areas/Identity/Pages/Account/.
// Replace its contents with the code below.

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _db;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext db)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _db = db;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? ReturnUrl { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        public class InputModel
        {
            [Required]
            [Display(Name = "University ID")]
            [StringLength(20)]
            public string UniversityId { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            [Display(Name = "University Email")]
            public string Email { get; set; } = string.Empty;

            [Required]
            [StringLength(100, MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "Passwords do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid) return Page();

            // ---------- Step 1: verify the University ID exists in mock data ---------
            // This is UC-01 / FR-06 — only registered university students can sign up.
            var student = await _db.Students
                .FirstOrDefaultAsync(s => s.UniversityId == Input.UniversityId);

            if (student is null)
            {
                ModelState.AddModelError(string.Empty,
                    "This University ID is not recognized. Please check with the registrar.");
                return Page();
            }

            // Step 2: make sure the email matches the one on file
            if (!string.Equals(student.UniversityEmail, Input.Email,
                               StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty,
                    "The email does not match the university record for this ID.");
                return Page();
            }

            // Step 3: prevent duplicate accounts for the same University ID (A1 of UC-01)
            var existing = await _userManager.Users
                .AnyAsync(u => u.UniversityId == Input.UniversityId);
            if (existing)
            {
                ModelState.AddModelError(string.Empty,
                    "An account already exists for this University ID. Please log in instead.");
                return Page();
            }

            // ---------- Step 4: create the Identity user ----------
            var user = new ApplicationUser
            {
                UniversityId = student.UniversityId,
                FullName = student.FullName,
            };

            await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
            await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

            var result = await _userManager.CreateAsync(user, Input.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return Page();
            }

            _logger.LogInformation("User created a new account: {Email}", Input.Email);

            // Optional: send the email confirmation. For local dev you can also skip this
            // and use builder.Services.Configure<IdentityOptions>(o => o.SignIn.RequireConfirmedAccount = false)
            // in Program.cs, then sign the user in directly.
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
                throw new NotSupportedException("Default UI requires a user store with email support.");
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
