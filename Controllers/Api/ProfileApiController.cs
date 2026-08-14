using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    /// <summary>
    /// Mobile-facing profile API — FR-06's editable fields and the read-only
    /// ones the app shows alongside them.
    ///
    /// The rules live in ProfileService, which the web controller calls too;
    /// this only translates outcomes into HTTP.
    ///
    /// Not restricted to Students: profile management is basic account
    /// housekeeping rather than a service, so any signed-in role may use it.
    /// </summary>
    [ApiController]
    [Route("api/profile")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class ProfileApiController : ControllerBase
    {
        private readonly ProfileService _service;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileApiController(ProfileService service, UserManager<ApplicationUser> userManager)
        {
            _service = service;
            _userManager = userManager;
        }

        public class ErrorResponse
        {
            public string Error { get; set; } = string.Empty;
            public string? Code { get; set; }
        }

        public class ProfileDto
        {
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string UniversityId { get; set; } = string.Empty;
            public string UniversityCode { get; set; } = string.Empty;
            public string? PhoneNumber { get; set; }

            /// <summary>
            /// Absolute URL, so the app can bind it straight to an Image. The
            /// stored value is root-relative and would resolve against nothing
            /// on a device.
            /// </summary>
            public string? ProfilePictureUrl { get; set; }

            /// <summary>Editable fields still empty — the app's completeness hint.</summary>
            public int MissingFields { get; set; }
        }

        public class UpdateProfileRequest
        {
            public string? PhoneNumber { get; set; }
        }

        public class ActionResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public string? ProfilePictureUrl { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            return Ok(ToDto(user));
        }

        /// <summary>
        /// Phone number only. The picture is a separate endpoint because it is
        /// multipart, and mixing the two would force every phone edit to be a
        /// multipart request.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _service.UpdateAsync(user, request?.PhoneNumber, picture: null);
            if (!result.Ok) return Refused(result);

            return Ok(new ActionResponse { Success = true, Message = result.Message });
        }

        [HttpPost("picture")]
        [RequestSizeLimit(4 * 1024 * 1024)]
        public async Task<IActionResult> UploadPicture(IFormFile? file)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            if (file is null || file.Length == 0)
                return BadRequest(new ErrorResponse { Error = "No image was uploaded.", Code = "EMPTY" });

            await using var content = file.OpenReadStream();

            var result = await _service.UpdateAsync(
                user,
                user.PhoneNumber,
                new ProfileService.PictureUpload(content, file.FileName, file.Length));

            if (!result.Ok) return Refused(result);

            return Ok(new ActionResponse
            {
                Success = true,
                Message = result.Message,
                ProfilePictureUrl = AbsoluteUrl(user.ProfilePicturePath)
            });
        }

        [HttpDelete("picture")]
        public async Task<IActionResult> RemovePicture()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _service.RemovePictureAsync(user);
            if (!result.Ok) return Refused(result);

            return Ok(new ActionResponse { Success = true, Message = result.Message });
        }

        // ---------- mapping ----------

        private ProfileDto ToDto(ApplicationUser user) => new()
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            UniversityId = user.UniversityId,
            UniversityCode = user.UniversityCode,
            PhoneNumber = user.PhoneNumber,
            ProfilePictureUrl = AbsoluteUrl(user.ProfilePicturePath),
            MissingFields =
                (string.IsNullOrWhiteSpace(user.PhoneNumber) ? 1 : 0)
                + (string.IsNullOrWhiteSpace(user.ProfilePicturePath) ? 1 : 0)
        };

        /// <summary>
        /// Turns "/uploads/profiles/x.png" into a URL the app can fetch. Built
        /// from the request rather than from configuration so it stays correct
        /// whichever host the app reached the server on.
        /// </summary>
        private string? AbsoluteUrl(string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return null;

            return $"{Request.Scheme}://{Request.Host}{storedPath}";
        }

        private IActionResult Refused(ProfileService.Result result) =>
            BadRequest(new ErrorResponse { Error = result.Message ?? "Request refused.", Code = result.Code });
    }
}
