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
using UniConnect.Adapters;
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
        private readonly IUniversityProviderResolver _providerResolver;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext db,
            IUniversityProviderResolver providerResolver)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _db = db;
            _providerResolver = providerResolver;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? ReturnUrl { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        // Populated on GET so the Register page can show a "Select University"
        // dropdown (per the multi-university design — see Front Design doc).
        public List<University> Universities { get; set; } = new();

        public class InputModel
        {
            [Required]
            [Display(Name = "University")]
            public string UniversityCode { get; set; } = string.Empty;

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
            Universities = await _db.Universities.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            Universities = await _db.Universities.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();

            if (!ModelState.IsValid) return Page();

            var selectedUniversity = Universities.FirstOrDefault(u => u.Code == Input.UniversityCode);
            if (selectedUniversity is null)
            {
                ModelState.AddModelError(string.Empty, "Please select a valid university.");
                return Page();
            }

            // ---------- Step 1: verify the University ID LIVE, through the adapter ---------
            // This is UC-01 / FR-06 — only registered university students can sign up.
            // Deliberately calls the university's API directly (through
            // IUniversityProvider) rather than checking the local Students
            // cache table — that cache is only ever populated by
            // UniversityApiSyncRunner, which some real partner universities'
            // APIs (anything other than "Simulated" — see University.ApiStyle)
            // aren't compatible with yet. A live check works the same way
            // regardless of which adapter a university uses, and matches how
            // every other feature (enrollment checks, attendance) already
            // reads academic data — the cache was the one inconsistent
            // exception, not the rule.
            UniversityStudentDto? student;
            try
            {
                var provider = await _providerResolver.GetProviderAsync(Input.UniversityCode);
                student = await provider.GetStudentInfoAsync(Input.UniversityCode, Input.UniversityId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live student lookup failed during registration for {University}/{Id}.",
                    Input.UniversityCode, Input.UniversityId);
                ModelState.AddModelError(string.Empty,
                    "We couldn't reach the university's system right now to verify this ID. Please try again shortly.");
                return Page();
            }

            if (student is null)
            {
                ModelState.AddModelError(string.Empty,
                    "This University ID is not recognized. Please check with the registrar.");
                return Page();
            }

            // Step 2: make sure the email matches the one on file. (No separate
            // "does this ID belong to the selected university" check is needed
            // here anymore — GetStudentInfoAsync above was already scoped to
            // Input.UniversityCode, so a null result already covers that case.)
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
                UniversityId = student.StudentNumber,
                UniversityCode = Input.UniversityCode,
                FullName = student.FullName,
                EmailConfirmed = false, // explicit: ownership isn't proven until they click the link we email them
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
            await _userManager.AddToRoleAsync(user, "Student");

            // ---------- Step 5: email confirmation (proves the registrant actually
            // controls this inbox — without this, anyone who merely KNOWS a
            // student's ID + email on file could register as them) ----------
            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code, returnUrl },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(Input.Email, "Confirm your UniConnect account",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>clicking here</a>.");

            return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
                throw new NotSupportedException("Default UI requires a user store with email support.");
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
