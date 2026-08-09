using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Builds the JWT a mobile client receives from POST /api/auth/login and
    /// sends back as "Authorization: Bearer &lt;token&gt;" on every later
    /// request. Deliberately mirrors the claims the web app already reads
    /// off the cookie principal in dozens of places (UserManager.GetUserAsync,
    /// User.IsInRole, etc.) — the same [Authorize(Roles = "...")] attributes
    /// and User.Identity-based code work unchanged for API controllers, since
    /// ASP.NET Core builds the same ClaimsPrincipal shape from either scheme.
    ///
    /// No refresh-token flow yet — the access token is simply long-lived
    /// (see Jwt:AccessTokenExpiryMinutes, default 7 days). A real production
    /// deployment would want short-lived access tokens + refresh tokens
    /// instead; noted here as a deliberate scope simplification for now,
    /// not an oversight — add it later if session-revocation speed matters
    /// (e.g. to make ToggleSuspend/suspension take effect immediately for
    /// mobile sessions the way SuspendedUserMiddleware does for the web).
    /// </summary>
    public class JwtTokenService
    {
        private readonly IConfiguration _config;

        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }

        public (string Token, DateTime ExpiresAtUtc) CreateToken(ApplicationUser user, IList<string> roles)
        {
            var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            var issuer = _config["Jwt:Issuer"] ?? "UniConnect";
            var audience = _config["Jwt:Audience"] ?? "UniConnectMobile";
            var expiryMinutes = _config.GetValue<int?>("Jwt:AccessTokenExpiryMinutes") ?? 10080; // 7 days

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(ClaimTypes.NameIdentifier, user.Id), // what UserManager.GetUserAsync/GetUserId read
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Email, user.Email ?? string.Empty),
                new("full_name", user.FullName),
                new("university_code", user.UniversityCode),
                new("university_id", user.UniversityId),
            };
            if (!string.IsNullOrWhiteSpace(user.Department))
                claims.Add(new Claim("department", user.Department));

            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
        }
    }
}
