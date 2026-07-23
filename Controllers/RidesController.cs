using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
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
    [UniConnect.Filters.RequireService(UniConnect.Models.ServiceCodes.RideSharing)]
    public class RidesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IGeocodingService _geocoder;
        private readonly IHubContext<RideTrackingHub> _trackingHub;
        private readonly UniConnect.Services.AuditLogService _auditLog;
        private readonly UniConnect.Services.NotificationService _notifications;

        public RidesController(ApplicationDbContext db,
                               UserManager<ApplicationUser> userManager,
                               IGeocodingService geocoder,
                               IHubContext<RideTrackingHub> trackingHub,
                               UniConnect.Services.AuditLogService auditLog,
                               UniConnect.Services.NotificationService notifications)
        {
            _db = db;
            _userManager = userManager;
            _geocoder = geocoder;
            _trackingHub = trackingHub;
            _auditLog = auditLog;
            _notifications = notifications;
        }

        // Notifies anyone currently viewing the Available Rides list that
        // something changed (new ride posted, a ride filled up/got cancelled/
        // completed, seats freed up, etc.) so their list can refresh live.
        private Task BroadcastRideListChanged()
            => _trackingHub.Clients.Group("rides-lobby").SendAsync("RideListChanged");

        // ---------- INDEX: browse available rides (FR-09) -------------------------
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Show active rides that still have seats, created by OTHER students
            // AT MY OWN UNIVERSITY, that haven't already departed.
            var rides = await _db.Rides
                .Include(r => r.Driver)
                .Include(r => r.Vehicle)
                .Where(r => r.UniversityCode == user.UniversityCode
                            && r.Status == RideStatus.Active
                            && r.AvailableSeats > 0
                            && r.DriverId != user.Id
                            && r.TripStartedAt == null
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
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // FR-55: a student must register at least one Active vehicle
            // before they can offer a ride — that registration IS what
            // "becoming a driver student" means here (see VehiclesController).
            var activeVehicles = await _db.Vehicles
                .Where(v => v.UserId == user.Id && v.Status == VehicleStatus.Active)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            if (activeVehicles.Count == 0)
            {
                TempData["Error"] = "You need to register a vehicle before you can offer a ride.";
                return RedirectToAction("Create", "Vehicles");
            }

            ViewBag.Vehicles = activeVehicles;
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

            // The selected vehicle must genuinely belong to this student and
            // still be Active — defends against a tampered form post picking
            // someone else's vehicle, or one they since deactivated.
            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(
                v => v.Id == vm.VehicleId && v.UserId == user.Id && v.Status == VehicleStatus.Active);
            if (vehicle is null)
                ModelState.AddModelError(nameof(vm.VehicleId), "Please select one of your registered, active vehicles.");
            else if (vm.TotalSeats > vehicle.SeatCapacity)
                ModelState.AddModelError(nameof(vm.TotalSeats), $"This vehicle only seats {vehicle.SeatCapacity}.");

            if (!ModelState.IsValid)
            {
                ViewBag.Vehicles = await _db.Vehicles
                    .Where(v => v.UserId == user.Id && v.Status == VehicleStatus.Active)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync();
                return View(vm);
            }

            var ride = new Ride
            {
                UniversityCode = user.UniversityCode,
                DriverId = user.Id,
                DepartureLocation = vm.DepartureLocation.Trim(),
                Destination = vm.Destination.Trim(),
                DepartureTime = vm.DepartureTime,
                VehicleId = vehicle!.Id,
                TotalSeats = vm.TotalSeats,
                AvailableSeats = vm.TotalSeats,   // all seats free at creation (FR-15)
                Status = RideStatus.Active,
                Notes = vm.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            // Geocode the addresses into coordinates for the map.
            // If the user dropped a pin on the map, those coordinates are
            // authoritative (they came straight from the click, not a guess).
            // We only fall back to text geocoding when no pin was set.
            if (vm.DepartureLat.HasValue && vm.DepartureLng.HasValue)
            {
                ride.DepartureLat = vm.DepartureLat;
                ride.DepartureLng = vm.DepartureLng;
            }
            else
            {
                var dep = await _geocoder.GeocodeAsync(ride.DepartureLocation);
                if (dep.HasValue) { ride.DepartureLat = dep.Value.lat; ride.DepartureLng = dep.Value.lng; }
            }

            if (vm.DestinationLat.HasValue && vm.DestinationLng.HasValue)
            {
                ride.DestinationLat = vm.DestinationLat;
                ride.DestinationLng = vm.DestinationLng;
            }
            else
            {
                var dest = await _geocoder.GeocodeAsync(ride.Destination);
                if (dest.HasValue) { ride.DestinationLat = dest.Value.lat; ride.DestinationLng = dest.Value.lng; }
            }

            _db.Rides.Add(ride);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "RideCreated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "Ride",
                entityId: ride.Id.ToString(),
                details: $"{ride.DepartureLocation} -> {ride.Destination}");

            await BroadcastRideListChanged();

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
                .Include(r => r.Vehicle)
                .Include(r => r.Requests).ThenInclude(rr => rr.Passenger)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (ride is null) return NotFound();

            // Adapter/Integration Edge Cases: "Cross-university data leak" —
            // never let a ride from another university be viewable, even by
            // direct URL/ID guessing.
            if (ride.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This ride doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

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
        public async Task<IActionResult> RequestRide(int id, string pickupLocation, double? pickupLat, double? pickupLng)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Edge Case: "Excessive ride requests — a student sends too many
            // ride requests in a short period. The system shall apply
            // request rate limits." FR-11: the actual limit/window is
            // configurable per university rather than hardcoded.
            var settings = await _db.UniversitySettings.FindAsync(user.UniversityCode);
            var maxRequests = settings?.MaxRideRequestsPerWindow ?? 5;
            var windowMinutes = settings?.RideRequestWindowMinutes ?? 10;

            var rateLimitWindow = DateTime.UtcNow.AddMinutes(-windowMinutes);
            var recentRequestCount = await _db.RideRequests.CountAsync(
                rr => rr.PassengerId == user.Id && rr.RequestedAt >= rateLimitWindow);
            if (recentRequestCount >= maxRequests)
            {
                TempData["Error"] = "You've sent a lot of ride requests recently — please wait a few minutes before requesting another.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (string.IsNullOrWhiteSpace(pickupLocation))
            {
                TempData["Error"] = "Please enter a valid pickup location.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var ride = await _db.Rides
                .Include(r => r.Requests)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (ride is null) return NotFound();

            // Same cross-university guard as Details.
            if (ride.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This ride doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

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

            // Once the driver has hit "Start Trip", no new passengers can join.
            if (ride.TripStartedAt.HasValue)
            {
                TempData["Error"] = "This ride has already started and can no longer accept requests.";
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

            // Geocode the pickup location for the map. A pin dropped directly on
            // the map is authoritative; only fall back to text geocoding otherwise.
            if (pickupLat.HasValue && pickupLng.HasValue)
            {
                newRequest.PickupLat = pickupLat;
                newRequest.PickupLng = pickupLng;
            }
            else
            {
                var pick = await _geocoder.GeocodeAsync(newRequest.PickupLocation);
                if (pick.HasValue) { newRequest.PickupLat = pick.Value.lat; newRequest.PickupLng = pick.Value.lng; }
            }

            _db.RideRequests.Add(newRequest);
            await _db.SaveChangesAsync();

            // Let the driver's already-open Details page see the new request
            // live, without needing a refresh.
            await _trackingHub.Clients.Group($"ride-{id}").SendAsync("NewRideRequest", new
            {
                requestId = newRequest.Id,
                passengerName = user.FullName ?? "A student"
            });
            await _notifications.NotifyAsync(
                ride.DriverId,
                "New ride request",
                $"{user.FullName} wants to join your ride to {ride.Destination}.",
                $"/Rides/Details/{ride.Id}");
            await BroadcastRideListChanged();

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

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Edge Case: "Double seat reservation" — the ride changed
                    // (another request accepted, cancelled, etc.) between our
                    // seat-count check and this save. Don't guess at the
                    // outcome — ask the driver to re-check current seats.
                    TempData["Error"] = "This ride changed while you were accepting that request — please check available seats and try again.";
                    return RedirectToAction(nameof(Details), new { id = request.RideId });
                }

                // Tell anyone viewing this ride's page — specifically the affected
                // passenger — that their status changed, so their already-open
                // Details page can refresh itself instead of needing a manual
                // reload to pick up live-tracking access (Phase 4).
                await _trackingHub.Clients.Group($"ride-{request.RideId}").SendAsync("RequestStatusChanged", new
                {
                    passengerId = request.PassengerId,
                    status = request.Status.ToString()
                });
                await BroadcastRideListChanged();

                await _auditLog.LogAsync(
                    "RideRequestAccepted",
                    userId: user.Id,
                    universityCode: user.UniversityCode,
                    entityType: "RideRequest",
                    entityId: request.Id.ToString(),
                    details: $"Ride {request.RideId}, passenger {request.PassengerId}");

                await _notifications.NotifyAsync(
                    request.PassengerId,
                    "Ride request accepted",
                    $"Your request to join the ride to {request.Ride.Destination} was accepted.",
                    $"/Rides/Details/{request.RideId}");

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

            await _trackingHub.Clients.Group($"ride-{request.RideId}").SendAsync("RequestStatusChanged", new
            {
                passengerId = request.PassengerId,
                status = request.Status.ToString()
            });
            await _notifications.NotifyAsync(
                request.PassengerId,
                "Ride request declined",
                $"Your request to join the ride to {request.Ride.Destination} was declined.",
                "/Rides/Index");
            await BroadcastRideListChanged();

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

            // Stop any live-tracking listeners — the trip is over.
            await _trackingHub.Clients.Group($"ride-{id}").SendAsync("TripEnded", "cancelled");
            await BroadcastRideListChanged();

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

            // Stop any live-tracking listeners — the trip is over.
            await _trackingHub.Clients.Group($"ride-{id}").SendAsync("TripEnded", "completed");
            await BroadcastRideListChanged();

            TempData["Success"] = "Ride marked as completed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- REVERSE GEOCODE (AJAX) — used by the map pin pickers -----------
        // Called from the browser when the user clicks/drags a pin on a map, so we
        // can show a readable address in the text field. Purely cosmetic — the
        // authoritative data is the lat/lng the pin was dropped at, not this text.
        [HttpGet]
        public async Task<IActionResult> ReverseGeocode(double lat, double lng)
        {
            var address = await _geocoder.ReverseGeocodeAsync(lat, lng);
            return Json(new { address });
        }

        // ---------- FORWARD GEOCODE (AJAX) — live address preview (Phase 3) -------
        // Called (debounced) while the user types an address, so we can show them
        // where it resolved to on the map *before* they submit — instead of only
        // finding out on the Details page afterwards.
        [HttpGet]
        public async Task<IActionResult> Geocode(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return Json(new { found = false });

            var result = await _geocoder.GeocodeAsync(address);
            if (result is null)
                return Json(new { found = false });

            return Json(new { found = true, lat = result.Value.lat, lng = result.Value.lng });
        }

        // ---------- START TRIP (POST) — Phase 4 live tracking ----------------------
        // Marks the trip as started. From this point: no new requests are accepted
        // (see RequestRide above), and the driver's browser begins streaming its
        // location to accepted passengers via the RideTrackingHub.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartTrip(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == id);
            if (ride is null) return NotFound();
            if (ride.DriverId != user.Id) return Forbid();

            if (ride.Status != RideStatus.Active && ride.Status != RideStatus.Full)
            {
                TempData["Error"] = "This ride can't be started right now.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!ride.TripStartedAt.HasValue)
            {
                ride.TripStartedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _trackingHub.Clients.Group($"ride-{id}").SendAsync("TripStarted");
                await BroadcastRideListChanged();
            }

            TempData["Success"] = "Trip started — your location is now shared with accepted passengers.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- UPDATE LOCATION (AJAX, POST) — Phase 4 live tracking -----------
        // Called repeatedly (every few seconds) by the driver's browser while a
        // trip is in progress. Validates that the caller is actually the driver
        // of this ride and that the trip has actually started, persists the last
        // known position (so late-joining passengers see it immediately), then
        // broadcasts it live to the ride's tracking group.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLocation(int rideId, double lat, double lng)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == rideId);
            if (ride is null) return NotFound();
            if (ride.DriverId != user.Id) return Forbid();
            if (!ride.TripStartedAt.HasValue) return BadRequest("Trip has not started.");

            ride.LastLat = lat;
            ride.LastLng = lng;
            ride.LastLocationAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _trackingHub.Clients.Group($"ride-{rideId}").SendAsync("ReceiveLocation", new
            {
                lat,
                lng,
                at = ride.LastLocationAt
            });

            return Ok();
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
