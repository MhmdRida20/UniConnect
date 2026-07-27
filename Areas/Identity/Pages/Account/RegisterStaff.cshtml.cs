// Areas/Identity/Pages/Account/RegisterStaff.cshtml.cs
//
// Mirrors Register.cshtml.cs / RegisterInstructor.cshtml.cs exactly —
// verifies a real staff ID + email against the local StaffRecord cache.
// Department comes from the verified record itself, never self-declared —
// a staff member can't claim a different department than the one their
// university's own records assign them to.

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
    public class RegisterStaffModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterStaffModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _db;

        public RegisterStaffModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterStaffModel> logger,
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

        public List<University> Universities { get; set; } = new();

        public class InputModel
        {
            [Required]
            [Display(Name = "University")]
            public string UniversityCode { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Staff ID")]
            [StringLength(20)]
            public string StaffId { get; set; } = string.Empty;

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
            Universities = await _db.Universities.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            Universities = await _db.Universities.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();

            if (!ModelState.IsValid) return Page();

            // Step 1: verify the Staff ID exists (synced staff data)
            var staff = await _db.StaffRecords
                .FirstOrDefaultAsync(s => s.StaffId == Input.StaffId);

            if (staff is null)
            {
                ModelState.AddModelError(string.Empty,
                    "This Staff ID is not recognized. Please check with your department.");
                return Page();
            }

            // Step 1b: selected university must match the one this staff
            // member actually belongs to.
            if (!string.Equals(staff.UniversityCode, Input.UniversityCode, StringComparison.Ordinal))
            {
                ModelState.AddModelError(string.Empty,
                    "This Staff ID does not belong to the selected university.");
                return Page();
            }

            // Step 2: email must match the one on file
            if (!string.Equals(staff.UniversityEmail, Input.Email,
                               StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty,
                    "The email does not match the university record for this Staff ID.");
                return Page();
            }

            // Step 3: prevent duplicate accounts for the same Staff ID
            var existing = await _userManager.Users
                .AnyAsync(u => u.UniversityId == Input.StaffId);
            if (existing)
            {
                ModelState.AddModelError(string.Empty,
                    "An account already exists for this Staff ID. Please log in instead.");
                return Page();
            }

            // ---------- Step 4: create the Identity user ----------
            // Department comes from the VERIFIED record, not anything the
            // registrant typed or chose — see this file's header comment.
            var user = new ApplicationUser
            {
                UniversityId = staff.StaffId,
                UniversityCode = staff.UniversityCode,
                FullName = staff.FullName,
                Department = staff.Department,
                EmailConfirmed = false,
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

            _logger.LogInformation("Staff member created a new account: {Email}", Input.Email);
            await _userManager.AddToRoleAsync(user, "DepartmentStaff");

            // ---------- Step 5: email confirmation ----------
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
