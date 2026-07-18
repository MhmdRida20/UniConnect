using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace UniConnect.Services
{
    /// <summary>
    /// Sends real email via SMTP, configured through the "Email" section of
    /// appsettings.json. This replaces the default no-op IEmailSender that
    /// ASP.NET Core Identity's scaffolded UI silently registers otherwise —
    /// which is why "email confirmation" previously did nothing at all: the
    /// framework was happily calling SendEmailAsync on an implementation that
    /// never actually sent anything.
    ///
    /// If no SMTP host is configured (the "Email:SmtpHost" setting is blank),
    /// this logs the full email — including the confirmation link — instead
    /// of silently failing. That keeps registration/email-confirmation fully
    /// working end-to-end for local development and demos without requiring
    /// real mail credentials: just check the console/output window for the
    /// link. Configure real SMTP (e.g. a Gmail app password, or a provider
    /// like SendGrid/Mailtrap) to actually deliver mail instead.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            var host = _config["Email:SmtpHost"];

            if (string.IsNullOrWhiteSpace(host))
            {
                // Pull any raw link(s) out of the HTML body so they can be
                // copy-pasted directly. The links inside the HTML are
                // HTML-ENCODED (e.g. "&" becomes "&amp;") — correct for a
                // real browser rendering the email, but not usable as-is if
                // a human is copy-pasting from this raw log text, so decode
                // them back to real URLs first.
                var links = System.Text.RegularExpressions.Regex.Matches(htmlMessage, "href='([^']+)'")
                    .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))
                    .ToList();
                var linksBlock = links.Count > 0
                    ? "\nLINK(S) — copy the FULL line below, nothing more/less:\n" + string.Join("\n", links)
                    : "";

                _logger.LogInformation(
                    "\n========== EMAIL (no SMTP configured — logged instead of sent) ==========\n" +
                    "To: {To}\nSubject: {Subject}\n{Body}{Links}\n" +
                    "==========================================================================",
                    toEmail, subject, htmlMessage, linksBlock);
                return;
            }

            var port = _config.GetValue<int?>("Email:SmtpPort") ?? 587;
            var username = _config["Email:Username"];
            var password = _config["Email:Password"];
            var fromAddress = _config["Email:FromAddress"] ?? username ?? "no-reply@uniconnect.local";
            var fromName = _config["Email:FromName"] ?? "UniConnect";
            var enableSsl = _config.GetValue<bool?>("Email:EnableSsl") ?? true;

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl
                };
                if (!string.IsNullOrWhiteSpace(username))
                    client.Credentials = new NetworkCredential(username, password);

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, fromName),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                // Never let a mail delivery failure silently swallow the
                // confirmation link — log it as a fallback so testing/demo
                // flows still work even if SMTP is misconfigured.
                _logger.LogError(ex, "Failed to send email to {To} via SMTP — logging content instead.", toEmail);
                _logger.LogInformation("EMAIL CONTENT (send failed):\nSubject: {Subject}\n{Body}", subject, htmlMessage);
            }
        }
    }
}
