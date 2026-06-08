using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.ViewModels;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Implements the Ride Sharing use cases:
    ///   UC-03 Create Ride        (FR-07, FR-08)
    ///   UC-04 Request Ride       (FR-09, FR-10, FR-11)
    ///   UC-05 Manage Requests    (FR-12, FR-15)
    ///   plus ride status tracking (FR-13) and cancellation (FR-14)
    /// </summary>
    [Authorize]
    public class RidesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IGeocodingService _geocoder;

        public RidesController(ApplicationDbContext db,
                               UserManager<ApplicationUser> userManager,
                               IGeocodingService geocoder)
        {
            _db = db;
            _userManager = userManager;
            _geocoder = geocoder;
        }

        // ---------- INDEX: browse available rides (FR-09) -------------------------
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Show active rides that still have seats, created by OTHER students,
            // and that haven't already departed.
            var rides = await _db.Rides
                .Include(r => r.Driver)
                .Where(r => r.Status == RideStatus.Active
                            && r.AvailableSeats > 0
                            && r.DriverId != user.Id
                            && r.DepartureTime > DateTime.Now)
                .OrderBy(r => r.DepartureTime)
                .ToListAsync();

            // For each ride, find out if the current user already has a pending/accepted request
            var myRequests = await _db.RideRequests
                .Where(rr => rr.PassengerId == user.Id)
                .ToListAsync();
            ViewBag.MyRequests = myRequests;

            return View(rides);
        }

        // ---------- CREATE (GET) — UC-03 ------------------------------------------
        public IActionResult Create()
        {
            return View(new RideCreateVM());
        }

        // ---------- CREATE (POST) — UC-03, FR-07 ----------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RideCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // E1 of UC-03 — basic validation
            if (vm.DepartureTime <= DateTime.Now)
                ModelState.AddModelError(nameof(vm.DepartureTime),
                    "Departure time must be in the future.");

            if (string.Equals(vm.DepartureLocation?.Trim(), vm.Destination?.Trim(),
                              StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(vm.Destination),
                    "Destination must be different from the departure location.");

            if (!ModelState.IsValid)
                return View(vm);

            var ride = new Ride
            {
                DriverId = user.Id,
                DepartureLocation = vm.DepartureLocation.Trim(),
                Destination = vm.Destination.Trim(),
                DepartureTime = vm.DepartureTime,
                VehicleType = vm.VehicleType.Trim(),
                TotalSeats = vm.TotalSeats,
                AvailableSeats = vm.TotalSeats,   // all seats free at creation (FR-15)
                Status = RideStatus.Active,
                Notes = vm.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            // Geocode the addresses into coordinates for the map
            var dep = await _geocoder.GeocodeAsync(ride.DepartureLocation);
            if (dep.HasValue) { ride.DepartureLat = dep.Value.lat; ride.DepartureLng = dep.Value.lng; }

            var dest = await _geocoder.GeocodeAsync(ride.Destination);
            if (dest.HasValue) { ride.DestinationLat = dest.Value.lat; ride.DestinationLng = dest.Value.lng; }

            _db.Rides.Add(ride);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Ride created successfully.";
            return RedirectToAction(nameof(Details), new { id = ride.Id });
        }

        // ---------- DETAILS --------------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var ride = await _db.Rides
                .Include(r => r.Driver)
                .Include(r => r.Requests).ThenInclude(rr => rr.Passenger)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (ride is null) return NotFound();

            ViewBag.IsDriver = ride.DriverId == user.Id;
            ViewBag.MyRequest = ride.Requests
                .FirstOrDefault(rr => rr.PassengerId == user.Id
                                   && rr.Status != RideRequestStatus.Cancelled);
            ViewBag.CurrentUserId = user.Id;

            return View(ride);
        }

        // ---------- REQUEST RIDE (POST) — UC-04, FR-10, FR-11 ---------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRide(int id, string pickupLocation)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (string.IsNullOrWhiteSpace(pickupLocation))
            {
                TempData["Error"] = "Please enter a valid pickup location.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var ride = await _db.Rides
                .Include(r => r.Requests)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (ride is null) return NotFound();

            // Can't request your own ride
            if (ride.DriverId == user.Id)
            {
                TempData["Error"] = "You cannot request your own ride.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // E1 of UC-04 — ride full or not active
            if (ride.Status != RideStatus.Active || ride.AvailableSeats <= 0)
            {
                TempData["Error"] = "This ride is no longer available.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Prevent duplicate active requests from the same passenger
            var existing = ride.Requests.FirstOrDefault(
                rr => rr.PassengerId == user.Id
                   && (rr.Status == RideRequestStatus.Pending
                    || rr.Status == RideRequestStatus.Accepted));
            if (existing != null)
            {
                TempData["Error"] = "You already have a request for this ride.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var newRequest = new RideRequest
            {
                RideId = id,
                PassengerId = user.Id,
                PickupLocation = pickupLocation.Trim(),
                Status = RideRequestStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            // Geocode the pickup location for the map
            var pick = await _geocoder.GeocodeAsync(newRequest.PickupLocation);
            if (pick.HasValue) { newRequest.PickupLat = pick.Value.lat; newRequest.PickupLng = pick.Value.lng; }

            _db.RideRequests.Add(newRequest);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Ride request sent to the driver.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- CANCEL OWN REQUEST (POST) — A1 of UC-04 -----------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var request = await _db.RideRequests
                .Include(rr => rr.Ride)
                .FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null) return NotFound();
            if (request.PassengerId != user.Id) return Forbid();

            // If it was accepted, free the seat back up (FR-15)
            if (request.Status == RideRequestStatus.Accepted && request.Ride != null)
            {
                request.Ride.AvailableSeats++;
                if (request.Ride.Status == RideStatus.Full)
                    request.Ride.Status = RideStatus.Active;
            }

            request.Status = RideRequestStatus.Cancelled;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Your request was cancelled.";
            return RedirectToAction(nameof(Details), new { id = request.RideId });
        }

        // ---------- MANAGE REQUESTS — accept (POST) — UC-05, FR-12, FR-15 ---------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var request = await _db.RideRequests
                .Include(rr => rr.Ride).ThenInclude(r => r!.Requests)
                .FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null || request.Ride is null) return NotFound();

            // Only the driver of the ride can accept
            if (request.Ride.DriverId != user.Id) return Forbid();

            // Edge case: ride became full while this request was pending
            if (request.Ride.AvailableSeats <= 0)
            {
                request.Status = RideRequestStatus.Rejected;
                request.Ride.Status = RideStatus.Full;
                await _db.SaveChangesAsync();
                TempData["Error"] = "No seats left — request was auto-rejected.";
                return RedirectToAction(nameof(Details), new { id = request.RideId });
            }

            if (request.Status == RideRequestStatus.Pending)
            {
                request.Status = RideRequestStatus.Accepted;
                request.Ride.AvailableSeats--;             // FR-15

                if (request.Ride.AvailableSeats == 0)
                    request.Ride.Status = RideStatus.Full;  // FR-13

                await _db.SaveChangesAsync();
                TempData["Success"] = "Request accepted.";
            }

            return RedirectToAction(nameof(Details), new { id = request.RideId });
        }

        // ---------- MANAGE REQUESTS — reject (POST) — UC-05, A1 -------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRequest(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var request = await _db.RideRequests
                .Include(rr => rr.Ride)
                .FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null || request.Ride is null) return NotFound();
            if (request.Ride.DriverId != user.Id) return Forbid();

            // If it was previously accepted, free the seat (FR-15)
            if (request.Status == RideRequestStatus.Accepted)
            {
                request.Ride.AvailableSeats++;
                if (request.Ride.Status == RideStatus.Full)
                    request.Ride.Status = RideStatus.Active;
            }

            request.Status = RideRequestStatus.Rejected;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Request rejected.";
            return RedirectToAction(nameof(Details), new { id = request.RideId });
        }

        // ---------- CANCEL RIDE (POST) — UC-05 E1, FR-14 --------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRide(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var ride = await _db.Rides
                .Include(r => r.Requests)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (ride is null) return NotFound();
            if (ride.DriverId != user.Id) return Forbid();

            ride.Status = RideStatus.Cancelled;

            // Auto-reject all pending/accepted requests (notify affected students — FR-14)
            foreach (var req in ride.Requests.Where(
                         r => r.Status == RideRequestStatus.Pending
                           || r.Status == RideRequestStatus.Accepted))
            {
                req.Status = RideRequestStatus.Rejected;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Ride cancelled. Affected passengers have been updated.";
            return RedirectToAction(nameof(MyRides));
        }

        // ---------- COMPLETE RIDE (POST) — FR-13 ----------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRide(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == id);
            if (ride is null) return NotFound();
            if (ride.DriverId != user.Id) return Forbid();

            ride.Status = RideStatus.Completed;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Ride marked as completed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- MY RIDES: rides I'm driving + rides I requested ---------------
        public async Task<IActionResult> MyRides()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Rides I created
            ViewBag.DrivingRides = await _db.Rides
                .Include(r => r.Requests)
                .Where(r => r.DriverId == user.Id)
                .OrderByDescending(r => r.DepartureTime)
                .ToListAsync();

            // Rides I requested a seat on
            var myRequests = await _db.RideRequests
                .Include(rr => rr.Ride).ThenInclude(r => r!.Driver)
                .Where(rr => rr.PassengerId == user.Id
                          && rr.Status != RideRequestStatus.Cancelled)
                .OrderByDescending(rr => rr.RequestedAt)
                .ToListAsync();

            return View(myRequests);
        }
    }
}
