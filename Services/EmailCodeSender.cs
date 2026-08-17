using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using UniConnect.Models;

namespace UniConnect.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Builds and sends the account-confirmation email.
    ///
    /// One place, because five call sites need exactly the same message: the
    /// three web registration pages, the mobile registration endpoint, and the
    /// resend handler. When this was a link it was duplicated across all of
    /// them, and the encode-then-build-URL dance had to be got right each time.
    ///
    /// Note there is no Base64Url encoding here. That existed only because a
    /// Data Protection token is not URL-safe; a six-digit code is sent raw, and
    /// encoding it would hand the user a base64 string to type.
    /// </summary>
    public static class EmailCodeSender
    {
        public static async Task SendAsync(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ApplicationUser user)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);

            // Wide letter-spacing and a large size because this is read off one
            // screen and typed into another - the usual reason a digit gets
            // misread is that 0/O and 1/l sit too close together.
            var body =
                "<p>Welcome to UniConnect. Your confirmation code is:</p>" +
                $"<p style=\"font-size:30px;font-weight:700;letter-spacing:.2em;margin:16px 0\">{code}</p>" +
                "<p>Enter it on the confirmation page to finish setting up your account.</p>" +
                "<p style=\"color:#64748b;font-size:13px\">The code expires after a few minutes. " +
                "If it stops working, request a new one from the same page.</p>";

            await emailSender.SendEmailAsync(user.Email!, "Your UniConnect confirmation code", body);
        }
    }
}
