// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using UniConnect.Models;

namespace UniConnect.Areas.Identity.Pages.Account.Manage
{
    public class Disable2faModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<Disable2faModel> _logger;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        public Disable2faModel(
            UserManager<ApplicationUser> userManager,
            ILogger<Disable2faModel> logger,
            UniConnect.Services.AuditLogService auditLog)
        {
            _userManager = userManager;
            _logger = logger;
            _auditLog = auditLog;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            // The scaffolded page throws here, which renders a 500 for the
            // ordinary cases of a bookmarked URL, a back button after
            // disabling, or a double-submitted form. Nothing has gone wrong —
            // the user simply has nothing to disable — so send them to the
            // status page instead.
            if (!await _userManager.GetTwoFactorEnabledAsync(user))
            {
                return RedirectToPage("./TwoFactorAuthentication");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            // Same reasoning as OnGet: a resubmitted form is not an error.
            if (!await _userManager.GetTwoFactorEnabledAsync(user))
            {
                return RedirectToPage("./TwoFactorAuthentication");
            }

            var disable2faResult = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (!disable2faResult.Succeeded)
            {
                // This one genuinely is unexpected — a store failure — but it
                // still should not be a 500 in the user's face.
                StatusMessage = "Error: two-factor could not be turned off. Please try again.";
                return RedirectToPage("./TwoFactorAuthentication");
            }

            _logger.LogInformation("User with ID '{UserId}' has disabled 2fa.", _userManager.GetUserId(User));

            await _auditLog.LogAsync(
                "TwoFactorDisabled",
                userId: user.Id, universityCode: user.UniversityCode,
                entityType: "User", entityId: user.Id,
                details: "Disabled two-factor authentication.");

            StatusMessage = "Two-factor authentication is off. You can turn it back on at any time.";
            return RedirectToPage("./TwoFactorAuthentication");
        }
    }
}
