using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    /// <summary>
    /// Mobile-facing Ride Sharing API. Write actions delegate to RideService
    /// (same rules RidesController uses); this controller only translates
    /// outcomes into HTTP and shapes read responses for the app.
    ///
    /// Deliberately not included here: StartTrip/UpdateLocation and the map
    /// pin geocoding endpoints — those belong to the live-tracking layer,
    /// which is a separate, later pass for the mobile app (REST first).
    /// </summary>
    [ApiController]
    [Route("api/rides")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class RidesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RideService _rideService;

        public RidesApiController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RideService rideService)
        {
            _db = db;
            _userManager = userManager;
            _rideService = rideService;
        }

        public class DriverDto
        {
            public string FullName { get; set; } = string.Empty;
        }

        public class VehicleSummaryDto
        {
            public string VehicleType { get; set; } = string.Empty;
            public string PlateNumber { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
        }

        public class RideListItemDto
        {
            public int Id { get; set; }
            public string DepartureLocation { get; set; } = string.Empty;
            public string Destination { get; set; } = string.Empty;
            public DateTime DepartureTime { get; set; }
            public int AvailableSeats { get; set; }
            public int TotalSeats { get; set; }
            public string Status { get; set; } = string.Empty;
            public DriverDto Driver { get; set; } = new();
            public VehicleSummaryDto? Vehicle { get; set; }
            public string? MyRequestStatus { get; set; }
        }

        public class RideRequestDto
        {
            public int Id { get; set; }
            public string PassengerName { get; set; } = string.Empty;
            public string PickupLocation { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime RequestedAt { get; set; }
        }

        public class RideDetailsDto : RideListItemDto
        {
            public string? Notes { get; set; }
            public bool IsDriver { get; set; }
            public List<RideRequestDto> Requests { get; set; } = new();
        }

        public class MyRidesResponse
        {
            public List<RideListItemDto> Driving { get; set; } = new();
            public List<RideRequestSummaryDto> Requested { get; set; } = new();
        }

        public class RideRequestSummaryDto
        {
            public int RequestId { get; set; }
            public int RideId { get; set; }
            public string DriverName { get; set; } = string.Empty;
            public string DepartureLocation { get; set; } = string.Empty;
            public string Destination { get; set; } = string.Empty;
            public DateTime DepartureTime { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        private static RideListItemDto ToListItem(Ride r, string? myStatus = null) => new()
        {
            Id = r.Id,
            DepartureLocation = r.DepartureLocation,
            Destination = r.Destination,
            DepartureTime = r.DepartureTime,
            AvailableSeats = r.AvailableSeats,
            TotalSeats = r.TotalSeats,
            Status = r.Status.ToString(),
            Driver = new DriverDto { FullName = r.Driver?.FullName ?? "Unknown" },
            Vehicle = r.Vehicle is null ? null : new VehicleSummaryDto
            {
                VehicleType = r.Vehicle.VehicleType,
                PlateNumber = r.Vehicle.PlateNumber,
                Color = r.Vehicle.Color
            },
            MyRequestStatus = myStatus
        };

        // ---------- BROWSE — same filter as RidesController.Index ----------
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var rides = await _db.Rides
                .Include(r => r.Driver).Include(r => r.Vehicle)
                .Where(r => r.UniversityCode == user.UniversityCode
                            && r.Status == RideStatus.Active
                            && r.AvailableSeats > 0
                            && r.DriverId != user.Id
                            && r.TripStartedAt == null
                            && r.DepartureTime > DateTime.Now)
                .OrderBy(r => r.DepartureTime)
                .ToListAsync();

            var myRequests = await _db.RideRequests
                .Where(rr => rr.PassengerId == user.Id && rr.Status != RideRequestStatus.Cancelled)
                .ToListAsync();

            var result = rides.Select(r =>
                ToListItem(r, myRequests.FirstOrDefault(rr => rr.RideId == r.Id)?.Status.ToString())).ToList();

            return Ok(result);
        }

        // ---------- DETAILS ----------
        [HttpGet("{id}")]
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var ride = await _db.Rides
                .Include(r => r.Driver).Include(r => r.Vehicle)
                .Include(r => r.Requests).ThenInclude(rr => rr.Passenger)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (ride is null) return NotFound(new { error = "This ride doesn't exist." });
            if (ride.UniversityCode != user.UniversityCode)
                return NotFound(new { error = "This ride doesn't belong to your university." });

            var isDriver = ride.DriverId == user.Id;
            var dto = new RideDetailsDto
            {
                Id = ride.Id,
                DepartureLocation = ride.DepartureLocation,
                Destination = ride.Destination,
                DepartureTime = ride.DepartureTime,
                AvailableSeats = ride.AvailableSeats,
                TotalSeats = ride.TotalSeats,
                Status = ride.Status.ToString(),
                Notes = ride.Notes,
                Driver = new DriverDto { FullName = ride.Driver?.FullName ?? "Unknown" },
                Vehicle = ride.Vehicle is null ? null : new VehicleSummaryDto
                {
                    VehicleType = ride.Vehicle.VehicleType, PlateNumber = ride.Vehicle.PlateNumber, Color = ride.Vehicle.Color
                },
                IsDriver = isDriver,
                MyRequestStatus = isDriver ? null : ride.Requests
                    .FirstOrDefault(rr => rr.PassengerId == user.Id && rr.Status != RideRequestStatus.Cancelled)?.Status.ToString(),
                // Requests are only meaningful to show the driver — a passenger
                // doesn't need to see who else requested this ride.
                Requests = isDriver
                    ? ride.Requests.Where(rr => rr.Status != RideRequestStatus.Cancelled).Select(rr => new RideRequestDto
                    {
                        Id = rr.Id,
                        PassengerName = rr.Passenger?.FullName ?? "Unknown",
                        PickupLocation = rr.PickupLocation,
                        Status = rr.Status.ToString(),
                        RequestedAt = rr.RequestedAt
                    }).ToList()
                    : new List<RideRequestDto>()
            };

            return Ok(dto);
        }

        // ---------- MY RIDES ----------
        [HttpGet("mine")]
        public async Task<IActionResult> Mine()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var driving = await _db.Rides.Include(r => r.Driver).Include(r => r.Vehicle).Include(r => r.Requests)
                .Where(r => r.DriverId == user.Id)
                .OrderByDescending(r => r.DepartureTime)
                .ToListAsync();

            var requested = await _db.RideRequests
                .Include(rr => rr.Ride).ThenInclude(r => r!.Driver)
                .Where(rr => rr.PassengerId == user.Id && rr.Status != RideRequestStatus.Cancelled)
                .OrderByDescending(rr => rr.RequestedAt)
                .ToListAsync();

            return Ok(new MyRidesResponse
            {
                Driving = driving.Select(r => ToListItem(r)).ToList(),
                Requested = requested.Where(rr => rr.Ride != null).Select(rr => new RideRequestSummaryDto
                {
                    RequestId = rr.Id,
                    RideId = rr.RideId,
                    DriverName = rr.Ride!.Driver?.FullName ?? "Unknown",
                    DepartureLocation = rr.Ride.DepartureLocation,
                    Destination = rr.Ride.Destination,
                    DepartureTime = rr.Ride.DepartureTime,
                    Status = rr.Status.ToString()
                }).ToList()
            });
        }

        // ---------- WRITE ACTIONS — all delegate to RideService ----------

        public class CreateRideRequest
        {
            public string DepartureLocation { get; set; } = string.Empty;
            public string Destination { get; set; } = string.Empty;
            public DateTime DepartureTime { get; set; }
            public int VehicleId { get; set; }
            public int TotalSeats { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateRideRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.CreateRideAsync(
                user, request.DepartureLocation, request.Destination, request.DepartureTime,
                request.VehicleId, request.TotalSeats, request.Notes);

            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message, rideId = result.Data });
        }

        public class RequestRideRequest
        {
            public string PickupLocation { get; set; } = string.Empty;
        }

        [HttpPost("{id}/request")]
        public async Task<IActionResult> RequestRide(int id, RequestRideRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.RequestRideAsync(user, id, request.PickupLocation);
            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("requests/{requestId}/cancel")]
        public async Task<IActionResult> CancelRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.CancelRequestAsync(user, requestId);
            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("requests/{requestId}/accept")]
        public async Task<IActionResult> AcceptRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.AcceptRequestAsync(user, requestId);
            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("requests/{requestId}/reject")]
        public async Task<IActionResult> RejectRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.RejectRequestAsync(user, requestId);
            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelRide(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _rideService.CancelRideAsync(user, id);
            if (!result.Ok) return BadRequest(new { error = result.Message });
            return Ok(new { success = true, message = result.Message });
        }
    }
}
