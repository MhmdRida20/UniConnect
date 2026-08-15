using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Controllers.Api
{
    /// <summary>
    /// Mobile-facing vehicle registration API — FR-55. A student's first
    /// Active vehicle is what qualifies them to offer rides at all (see
    /// RidesApiController.Create / RideService.CreateRideAsync); there's no
    /// separate "driver" role or flag.
    /// </summary>
    [ApiController]
    [Route("api/vehicles")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class VehiclesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        public VehiclesApiController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _auditLog = auditLog;
        }

        public class VehicleDto
        {
            public int Id { get; set; }
            public string VehicleType { get; set; } = string.Empty;
            public string PlateNumber { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
            public int SeatCapacity { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var vehicles = await _db.Vehicles
                .Where(v => v.UserId == user.Id)
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new VehicleDto
                {
                    Id = v.Id, VehicleType = v.VehicleType, PlateNumber = v.PlateNumber,
                    Color = v.Color, SeatCapacity = v.SeatCapacity, Status = v.Status.ToString()
                })
                .ToListAsync();

            return Ok(vehicles);
        }

        public class CreateVehicleRequest
        {
            public string VehicleType { get; set; } = string.Empty;
            public string PlateNumber { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
            public int SeatCapacity { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateVehicleRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.VehicleType) || string.IsNullOrWhiteSpace(request.PlateNumber) || string.IsNullOrWhiteSpace(request.Color))
                return BadRequest(new { error = "Vehicle type, plate number, and color are all required." });
            if (request.SeatCapacity is < 1 or > 8)
                return BadRequest(new { error = "Seat capacity must be between 1 and 8." });

            var vehicle = new Vehicle
            {
                UserId = user.Id,
                VehicleType = request.VehicleType.Trim(),
                PlateNumber = request.PlateNumber.Trim(),
                Color = request.Color.Trim(),
                SeatCapacity = request.SeatCapacity,
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.Vehicles.Add(vehicle);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync("VehicleRegistered", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "Vehicle", entityId: vehicle.Id.ToString(), details: $"{vehicle.VehicleType}, plate {vehicle.PlateNumber}");

            return Ok(new { success = true, message = "Vehicle registered — you can now offer rides with it.", vehicleId = vehicle.Id });
        }

        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == user.Id);
            if (vehicle is null) return NotFound(new { error = "Vehicle not found." });

            vehicle.Status = vehicle.Status == VehicleStatus.Active ? VehicleStatus.Inactive : VehicleStatus.Active;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, status = vehicle.Status.ToString() });
        }
    }
}
