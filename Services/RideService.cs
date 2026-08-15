using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Ride Sharing write rules (UC-03, UC-04, UC-05), mirrored from
    /// RidesController.cs exactly — same validation, same order of checks,
    /// same concurrency handling. Built as a separate service rather than by
    /// refactoring RidesController itself: that controller also owns the
    /// SignalR live-tracking calls (StartTrip/UpdateLocation) and the
    /// map/geocoding endpoints, none of which the mobile REST-first pass
    /// touches yet (see the project's live-layer plan) — pulling only the
    /// data-mutation rules out cleanly, without disturbing a large, already
    /// working file, was judged the lower-risk path for this pass.
    ///
    /// Known follow-up, not done here: RidesController itself should
    /// eventually call this service too, the same way AttendanceController
    /// now calls AttendanceSubmissionService — until then, the rules exist
    /// in two places and must be kept in sync by hand if either changes.
    /// </summary>
    public class RideService
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeocodingService _geocoder;
        private readonly NotificationService _notifications;
        private readonly AuditLogService _auditLog;

        public RideService(
            ApplicationDbContext db,
            IGeocodingService geocoder,
            NotificationService notifications,
            AuditLogService auditLog)
        {
            _db = db;
            _geocoder = geocoder;
            _notifications = notifications;
            _auditLog = auditLog;
        }

        public record Result(bool Ok, string Message, object? Data = null);

        // ---------- CREATE RIDE — UC-03, FR-07 ----------------------------------
        public async Task<Result> CreateRideAsync(
            ApplicationUser user, string departureLocation, string destination,
            DateTime departureTime, int vehicleId, int totalSeats, string? notes)
        {
            if (departureTime <= DateTime.Now)
                return new Result(false, "Departure time must be in the future.");

            var departure = departureLocation.Trim();
            var dest = destination.Trim();
            if (string.Equals(departure, dest, StringComparison.OrdinalIgnoreCase))
                return new Result(false, "Destination must be different from the departure location.");

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(
                v => v.Id == vehicleId && v.UserId == user.Id && v.Status == VehicleStatus.Active);
            if (vehicle is null)
                return new Result(false, "Please select one of your registered, active vehicles.");
            if (totalSeats > vehicle.SeatCapacity)
                return new Result(false, $"This vehicle only seats {vehicle.SeatCapacity}.");

            var ride = new Ride
            {
                UniversityCode = user.UniversityCode,
                DriverId = user.Id,
                DepartureLocation = departure,
                Destination = dest,
                DepartureTime = departureTime,
                VehicleId = vehicle.Id,
                TotalSeats = totalSeats,
                AvailableSeats = totalSeats,
                Status = RideStatus.Active,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            // Best-effort geocoding for the map — a missing/failed lookup
            // still lets the ride get created with text-only locations.
            var dep = await _geocoder.GeocodeAsync(ride.DepartureLocation);
            if (dep.HasValue) { ride.DepartureLat = dep.Value.lat; ride.DepartureLng = dep.Value.lng; }
            var destGeo = await _geocoder.GeocodeAsync(ride.Destination);
            if (destGeo.HasValue) { ride.DestinationLat = destGeo.Value.lat; ride.DestinationLng = destGeo.Value.lng; }

            _db.Rides.Add(ride);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync("RideCreated", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "Ride", entityId: ride.Id.ToString(), details: $"{ride.DepartureLocation} -> {ride.Destination}");

            return new Result(true, "Ride created successfully.", ride.Id);
        }

        // ---------- REQUEST RIDE — UC-04, FR-10, FR-11 --------------------------
        public async Task<Result> RequestRideAsync(ApplicationUser user, int rideId, string pickupLocation)
        {
            if (string.IsNullOrWhiteSpace(pickupLocation))
                return new Result(false, "Please enter a valid pickup location.");

            var settings = await _db.UniversitySettings.FindAsync(user.UniversityCode);
            var maxRequests = settings?.MaxRideRequestsPerWindow ?? 5;
            var windowMinutes = settings?.RideRequestWindowMinutes ?? 10;
            var rateLimitWindow = DateTime.UtcNow.AddMinutes(-windowMinutes);
            var recentRequestCount = await _db.RideRequests.CountAsync(
                rr => rr.PassengerId == user.Id && rr.RequestedAt >= rateLimitWindow);
            if (recentRequestCount >= maxRequests)
                return new Result(false, "You've sent a lot of ride requests recently — please wait a few minutes before requesting another.");

            var ride = await _db.Rides.Include(r => r.Requests).FirstOrDefaultAsync(r => r.Id == rideId);
            if (ride is null) return new Result(false, "This ride no longer exists.");
            if (ride.UniversityCode != user.UniversityCode) return new Result(false, "This ride doesn't belong to your university.");
            if (ride.DriverId == user.Id) return new Result(false, "You cannot request your own ride.");
            if (ride.Status != RideStatus.Active || ride.AvailableSeats <= 0) return new Result(false, "This ride is no longer available.");
            if (ride.TripStartedAt.HasValue) return new Result(false, "This ride has already started and can no longer accept requests.");

            var existing = ride.Requests.FirstOrDefault(rr =>
                rr.PassengerId == user.Id && (rr.Status == RideRequestStatus.Pending || rr.Status == RideRequestStatus.Accepted));
            if (existing != null) return new Result(false, "You already have a request for this ride.");

            var newRequest = new RideRequest
            {
                RideId = rideId,
                PassengerId = user.Id,
                PickupLocation = pickupLocation.Trim(),
                Status = RideRequestStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            var pick = await _geocoder.GeocodeAsync(newRequest.PickupLocation);
            if (pick.HasValue) { newRequest.PickupLat = pick.Value.lat; newRequest.PickupLng = pick.Value.lng; }

            _db.RideRequests.Add(newRequest);
            await _db.SaveChangesAsync();

            await _notifications.NotifyAsync(ride.DriverId, "New ride request",
                $"{user.FullName} wants to join your ride to {ride.Destination}.", $"/Rides/Details/{ride.Id}");

            return new Result(true, "Ride request sent to the driver.", newRequest.Id);
        }

        // ---------- CANCEL OWN REQUEST — A1 of UC-04 ----------------------------
        public async Task<Result> CancelRequestAsync(ApplicationUser user, int requestId)
        {
            var request = await _db.RideRequests.Include(rr => rr.Ride).FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null) return new Result(false, "That request no longer exists.");
            if (request.PassengerId != user.Id) return new Result(false, "This isn't your request.");

            if (request.Status == RideRequestStatus.Accepted && request.Ride != null)
            {
                request.Ride.AvailableSeats++;
                if (request.Ride.Status == RideStatus.Full) request.Ride.Status = RideStatus.Active;
            }

            request.Status = RideRequestStatus.Cancelled;
            await _db.SaveChangesAsync();

            return new Result(true, "Your request was cancelled.");
        }

        // ---------- ACCEPT REQUEST — UC-05, FR-12, FR-15 ------------------------
        public async Task<Result> AcceptRequestAsync(ApplicationUser user, int requestId)
        {
            var request = await _db.RideRequests
                .Include(rr => rr.Ride).ThenInclude(r => r!.Requests)
                .FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null || request.Ride is null) return new Result(false, "That request no longer exists.");
            if (request.Ride.DriverId != user.Id) return new Result(false, "Only the driver of this ride can accept requests.");

            if (request.Ride.AvailableSeats <= 0)
            {
                request.Status = RideRequestStatus.Rejected;
                request.Ride.Status = RideStatus.Full;
                await _db.SaveChangesAsync();
                return new Result(false, "No seats left — request was auto-rejected.");
            }

            if (request.Status != RideRequestStatus.Pending)
                return new Result(true, "Request already handled.");

            request.Status = RideRequestStatus.Accepted;
            request.Ride.AvailableSeats--;
            if (request.Ride.AvailableSeats == 0) request.Ride.Status = RideStatus.Full;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return new Result(false, "This ride changed while you were accepting that request — please check available seats and try again.");
            }

            await _auditLog.LogAsync("RideRequestAccepted", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "RideRequest", entityId: request.Id.ToString(), details: $"Ride {request.RideId}, passenger {request.PassengerId}");
            await _notifications.NotifyAsync(request.PassengerId, "Ride request accepted",
                $"Your request to join the ride to {request.Ride.Destination} was accepted.", $"/Rides/Details/{request.RideId}");

            return new Result(true, "Request accepted.");
        }

        // ---------- REJECT REQUEST — UC-05, A1 -----------------------------------
        public async Task<Result> RejectRequestAsync(ApplicationUser user, int requestId)
        {
            var request = await _db.RideRequests.Include(rr => rr.Ride).FirstOrDefaultAsync(rr => rr.Id == requestId);
            if (request is null || request.Ride is null) return new Result(false, "That request no longer exists.");
            if (request.Ride.DriverId != user.Id) return new Result(false, "Only the driver of this ride can reject requests.");

            if (request.Status == RideRequestStatus.Accepted)
            {
                request.Ride.AvailableSeats++;
                if (request.Ride.Status == RideStatus.Full) request.Ride.Status = RideStatus.Active;
            }

            request.Status = RideRequestStatus.Rejected;
            await _db.SaveChangesAsync();

            await _notifications.NotifyAsync(request.PassengerId, "Ride request declined",
                $"Your request to join the ride to {request.Ride.Destination} was declined.", "/Rides/Index");

            return new Result(true, "Request rejected.");
        }

        // ---------- CANCEL RIDE — UC-05 E1, FR-14 --------------------------------
        public async Task<Result> CancelRideAsync(ApplicationUser user, int rideId)
        {
            var ride = await _db.Rides.Include(r => r.Requests).FirstOrDefaultAsync(r => r.Id == rideId);
            if (ride is null) return new Result(false, "This ride no longer exists.");
            if (ride.DriverId != user.Id) return new Result(false, "Only the driver of this ride can cancel it.");

            ride.Status = RideStatus.Cancelled;
            foreach (var req in ride.Requests.Where(r => r.Status == RideRequestStatus.Pending || r.Status == RideRequestStatus.Accepted))
                req.Status = RideRequestStatus.Rejected;

            await _db.SaveChangesAsync();

            return new Result(true, "Ride cancelled. Affected passengers have been updated.");
        }
    }
}
