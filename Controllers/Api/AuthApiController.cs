using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly JwtTokenService _tokens;
        private readonly AuditLogService _auditLog;

        // Only Student accounts ever receive a mobile token — Instructor,
        // Admin/UniversityAdmin/DepartmentStaff/Company all stay on the web
        // portal exclusively. (Earlier draft allowed Instructor too;
        // narrowed to Student-only per later decision.)
        private static readonly string[] MobileAllowedRoles = { "Student" };

        public AuthApiController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            JwtTokenService tokens,
            AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _tokens = tokens;
            _auditLog = auditLog;
        }

        public class LoginRequest
        {
            [Required, EmailAddress] public string Email { get; set; } = string.Empty;
            [Required] public string Password { get; set; } = string.Empty;
        }

        public class AuthResponse
        {
            public string Token { get; set; } = string.Empty;
            public DateTime ExpiresAtUtc { get; set; }
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string UniversityCode { get; set; } = string.Empty;
            public string UniversityId { get; set; } = string.Empty;
            public List<string> Roles { get; set; } = new();
        }

        // ---------- LOGIN ------------------------------------------------------
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);

            // CheckPasswordSignInAsync (not PasswordSignInAsync) deliberately —
            // it validates the password and applies lockout the same way the
            // web login does, WITHOUT writing a cookie, which is meaningless
            // for a stateless API client.
            var result = user is null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

            if (!result.Succeeded || user is null)
            {
                var reason = result.IsLockedOut ? "LockedOut"
                    : result.IsNotAllowed ? "NotAllowed (unconfirmed email or suspended)"
                    : "InvalidCredentials";

                await _auditLog.LogAsync(
                    "FailedLogin",
                    userId: user?.Id,
                    universityCode: user?.UniversityCode,
                    entityType: "User",
                    entityId: user?.Id,
                    details: $"[Mobile] Email attempted: {request.Email}; reason: {reason}");

                if (result.IsLockedOut)
                    return StatusCode(423, new { error = "locked_out", message = "Too many failed attempts. Try again later." });

                return Unauthorized(new { error = "invalid_credentials", message = "Incorrect email or password." });
            }

            if (user.IsSuspended)
            {
                await _auditLog.LogAsync("FailedLogin", userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id, details: "[Mobile] Account is suspended.");
                return StatusCode(403, new { error = "suspended", message = "This account has been suspended." });
            }

            // CheckPasswordSignInAsync above validates the password and applies
            // lockout, but it ignores TwoFactorEnabled entirely — so without
            // this block a user who turned on 2FA on the web would still be let
            // in here with a password alone, and the second factor would be
            // decorative rather than real.
            //
            // Refusing is the honest option while the app has no code-entry
            // screen. The enrolment page says so before the user opts in, so
            // this is a stated consequence rather than a surprise. Accepting an
            // optional totpCode in this request is the long-term answer; it
            // needs a mobile release, so it is deliberately not done here.
            // See docs/TWO_FACTOR_PLAN.md step 7.
            if (user.TwoFactorEnabled)
            {
                await _auditLog.LogAsync("FailedLogin", userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "[Mobile] Refused: account has two-factor authentication enabled.");

                return StatusCode(403, new
                {
                    error = "two_factor_required",
                    message = "This account uses two-factor authentication, which the mobile app does not support yet. Please sign in on the web portal."
                });
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (!roles.Any(r => MobileAllowedRoles.Contains(r)))
            {
                return StatusCode(403, new
                {
                    error = "role_not_supported",
                    message = "The UniConnect mobile app is for students. Instructors, staff, admin, and company accounts should use the web portal."
                });
            }

            var (token, expiresAtUtc) = _tokens.CreateToken(user, roles);

            await _auditLog.LogAsync("Login", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "User", entityId: user.Id, details: "[Mobile]");

            return Ok(new AuthResponse
            {
                Token = token,
                ExpiresAtUtc = expiresAtUtc,
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                UniversityCode = user.UniversityCode,
                UniversityId = user.UniversityId,
                Roles = roles.ToList()
            });
        }

        public class RegisterStudentRequest
        {
            [Required] public string UniversityCode { get; set; } = string.Empty;
            [Required] public string UniversityId { get; set; } = string.Empty;
            [Required, EmailAddress] public string Email { get; set; } = string.Empty;
            [Required, StringLength(100, MinimumLength = 6)] public string Password { get; set; } = string.Empty;
        }

        // ---------- STUDENT SELF-REGISTRATION -----------------------------------
        // Identical verification rules to Areas/Identity/Pages/Account/Register
        // .cshtml.cs — see that file for the reasoning behind each step. Kept as
        // a straight port rather than a shared helper for now, so the two entry
        // points (web page, mobile API) can be read and reasoned about
        // independently; worth extracting to a shared service if they start to
        // drift apart.
        [HttpPost("register/student")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var student = await _db.Students.FirstOrDefaultAsync(s => s.UniversityId == request.UniversityId);
            if (student is null)
                return BadRequest(new { error = "unknown_university_id", message = "This University ID is not recognized. Please check with the registrar." });

            if (!string.Equals(student.UniversityCode, request.UniversityCode, StringComparison.Ordinal))
                return BadRequest(new { error = "university_mismatch", message = "This University ID does not belong to the selected university." });

            if (!string.Equals(student.UniversityEmail, request.Email, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "email_mismatch", message = "The email does not match the university record for this ID." });

            if (await _userManager.Users.AnyAsync(u => u.UniversityId == request.UniversityId))
                return Conflict(new { error = "account_exists", message = "An account already exists for this University ID. Please log in instead." });

            var user = new ApplicationUser
            {
                UniversityId = student.UniversityId,
                UniversityCode = student.UniversityCode,
                FullName = student.FullName,
                UserName = request.Email,
                Email = request.Email,
                EmailConfirmed = false,
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return BadRequest(new { error = "create_failed", message = string.Join(" ", createResult.Errors.Select(e => e.Description)) });

            await _userManager.AddToRoleAsync(user, "Student");
            await SendConfirmationEmailAsync(user);

            return Ok(new { message = "Account created. Check your email to confirm your account before logging in." });
        }

        // Instructor mobile registration removed — instructors don't get
        // mobile access at all (Student-only, per decision). They continue
        // registering exactly as before, unaffected, through the existing
        // web page: Areas/Identity/Pages/Account/RegisterInstructor.cshtml.cs.

        // The same 6-digit code the web flow sends, from the same helper.
        // Confirmation itself is POST /api/auth/confirm-email below, so a
        // student who signs up in the app never has to leave it.
        private Task SendConfirmationEmailAsync(ApplicationUser user) =>
            UniConnect.Areas.Identity.Pages.Account.EmailCodeSender.SendAsync(
                _userManager, _emailSender, user);

        // ---------- CONFIRM EMAIL ----------------------------------------------
        // Mirrors the web ConfirmEmail page, including its attempt limiting:
        // six digits with a few minutes' life is guessable at volume, and
        // ConfirmEmailAsync counts nothing by itself.
        [HttpPost("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);

            // Same response whether or not the account exists — otherwise this
            // endpoint enumerates registered addresses.
            if (user is null)
                return BadRequest(new { error = "invalid_code", message = "That code is not valid or has expired." });

            if (user.EmailConfirmed)
                return Ok(new { message = "This account is already confirmed. You can sign in." });

            if (await _userManager.IsLockedOutAsync(user))
                return StatusCode(423, new { error = "locked_out", message = "Too many incorrect codes. Try again later." });

            var result = await _userManager.ConfirmEmailAsync(user, request.Code);

            if (!result.Succeeded)
            {
                await _userManager.AccessFailedAsync(user);

                await _auditLog.LogAsync("FailedLogin", userId: user.Id, universityCode: user.UniversityCode,
                    entityType: "User", entityId: user.Id,
                    details: "[Mobile] Invalid email confirmation code.");

                return BadRequest(new { error = "invalid_code", message = "That code is not valid or has expired." });
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            // Makes the code single-use: ConfirmEmailAsync leaves the security
            // stamp alone, so the code would otherwise stay valid to the end of
            // its window.
            await _userManager.UpdateSecurityStampAsync(user);

            await _auditLog.LogAsync("EmailConfirmed", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "User", entityId: user.Id, details: "[Mobile] Email confirmed by code.");

            return Ok(new { message = "Your email is confirmed. You can now sign in." });
        }

        // ---------- RESEND CONFIRMATION CODE -----------------------------------
        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is not null && !user.EmailConfirmed)
                await SendConfirmationEmailAsync(user);

            // Unconditional response, for the same enumeration reason.
            return Ok(new { message = "If that address needs confirming, a new code is on its way." });
        }

        public class ConfirmEmailRequest
        {
            [Required, EmailAddress] public string Email { get; set; } = string.Empty;
            [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
            public string Code { get; set; } = string.Empty;
        }

        public class ResendConfirmationRequest
        {
            [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        }
    }
}
