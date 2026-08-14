using Microsoft.AspNetCore.Identity;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// FR-06 — the editable half of a user's profile.
    ///
    /// Name, university ID and email are read-only everywhere: they come from
    /// the university's own records through the adapter, not from the user. Only
    /// the phone number and the picture are genuinely theirs to change.
    ///
    /// The rules moved here from ProfileController when the mobile app needed
    /// the same feature — the extension whitelist, the size cap and the
    /// delete-the-old-file step are exactly the sort of thing that drifts when
    /// two controllers each keep their own copy.
    /// </summary>
    public class ProfileService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly AuditLogService _auditLog;

        public ProfileService(
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            AuditLogService auditLog)
        {
            _userManager = userManager;
            _env = env;
            _auditLog = auditLog;
        }

        public static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg" };
        public const long MaxImageBytes = 2 * 1024 * 1024;

        public enum Outcome { Success, Refused }

        public record Result(Outcome Outcome, string? Message = null, string? Code = null)
        {
            public bool Ok => Outcome == Outcome.Success;
            public static Result Success(string message) => new(Outcome.Success, message);
            public static Result Refused(string message, string code) => new(Outcome.Refused, message, code);
        }

        /// <summary>An uploaded picture, decoupled from IFormFile so any caller can pass one.</summary>
        public record PictureUpload(Stream Content, string FileName, long Length);

        /// <summary>
        /// Saves the phone number and, if one was supplied, the new picture.
        /// A refused picture leaves the phone number unsaved too, so the caller
        /// never has to explain a half-applied edit.
        /// </summary>
        public async Task<Result> UpdateAsync(ApplicationUser user, string? phoneNumber, PictureUpload? picture)
        {
            if (picture is not null)
            {
                var check = Validate(picture);
                if (!check.Ok) return check;
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();

            if (picture is not null)
                user.ProfilePicturePath = await StoreAsync(user, picture);

            await _userManager.UpdateAsync(user);

            await _auditLog.LogAsync(
                "ProfileUpdated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id);

            return Result.Success("Profile updated.");
        }

        private static Result Validate(PictureUpload picture)
        {
            var ext = Path.GetExtension(picture.FileName).ToLowerInvariant();

            if (!AllowedImageExtensions.Contains(ext))
                return Result.Refused(
                    "Unsupported image format — please upload a PNG or JPEG.", "BAD_FORMAT");

            if (picture.Length > MaxImageBytes)
                return Result.Refused(
                    $"That image is too large — the maximum is {MaxImageBytes / (1024 * 1024)} MB.", "TOO_LARGE");

            if (picture.Length <= 0)
                return Result.Refused("That file is empty.", "EMPTY");

            return Result.Success(string.Empty);
        }

        /// <summary>Writes the new file and deletes the one it replaces.</summary>
        private async Task<string> StoreAsync(ApplicationUser user, PictureUpload picture)
        {
            DeleteExistingPicture(user);

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsDir);

            var storedName = $"{Guid.NewGuid()}{Path.GetExtension(picture.FileName).ToLowerInvariant()}";

            await using (var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create))
                await picture.Content.CopyToAsync(stream);

            return $"/uploads/profiles/{storedName}";
        }

        public async Task<Result> RemovePictureAsync(ApplicationUser user)
        {
            if (string.IsNullOrWhiteSpace(user.ProfilePicturePath))
                return Result.Success("There was no picture to remove.");

            DeleteExistingPicture(user);

            user.ProfilePicturePath = null;
            await _userManager.UpdateAsync(user);

            return Result.Success("Profile picture removed.");
        }

        /// <summary>
        /// Deletes the file behind ProfilePicturePath, if there is one. Failing
        /// to delete is not worth blocking the update over — the record is what
        /// matters, and an orphaned file is only wasted disk.
        /// </summary>
        private void DeleteExistingPicture(ApplicationUser user)
        {
            if (string.IsNullOrWhiteSpace(user.ProfilePicturePath)) return;

            try
            {
                var path = Path.Combine(_env.WebRootPath, user.ProfilePicturePath.TrimStart('/'));
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Locked or already gone. The new path overwrites the record either way.
            }
        }
    }
}
