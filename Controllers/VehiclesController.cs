using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-55: "The system shall allow a student to register as a driver
    /// student by adding vehicle information." A student registering their
    /// first vehicle here is effectively what "becoming a driver student"
    /// means in this implementation — there's no separate role flag,
    /// since having at least one Active vehicle IS the qualification
    /// RidesController.Create checks for.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.RideSharing)]
    public class VehiclesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditLogService _auditLog;

        public VehiclesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _auditLog = auditLog;
        }

        // ---------- INDEX: my registered vehicles -------------------------------
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var vehicles = await _db.Vehicles
                .Where(v => v.UserId == user.Id)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            // A vehicle currently used by an active/upcoming ride can't be
            // deleted — surfaced here so the "Delete" button can be disabled
            // with an explanatory tooltip rather than just failing silently.
            var vehicleIdsInUse = await _db.Rides
                .Where(r => r.Status == RideStatus.Active || r.Status == RideStatus.Full)
                .Select(r => r.VehicleId)
                .Distinct()
                .ToListAsync();
            ViewBag.VehicleIdsInUse = vehicleIdsInUse;

            return View(vehicles);
        }

        // ---------- REGISTER (GET/POST) -----------------------------------------
        public IActionResult Create() => View(new VehicleCreateVM());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VehicleCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!ModelState.IsValid) return View(vm);

            var vehicle = new Vehicle
            {
                UserId = user.Id,
                VehicleType = vm.VehicleType.Trim(),
                PlateNumber = vm.PlateNumber.Trim(),
                Color = vm.Color.Trim(),
                SeatCapacity = vm.SeatCapacity,
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.Vehicles.Add(vehicle);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "VehicleRegistered",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "Vehicle",
                entityId: vehicle.Id.ToString(),
                details: $"{vehicle.VehicleType}, plate {vehicle.PlateNumber}");

            TempData["Success"] = "Vehicle registered — you can now offer rides with it.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- TOGGLE STATUS (Active/Inactive) -----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == user.Id);
            if (vehicle is null) return NotFound();

            vehicle.Status = vehicle.Status == VehicleStatus.Active ? VehicleStatus.Inactive : VehicleStatus.Active;
            await _db.SaveChangesAsync();

            TempData["Success"] = vehicle.Status == VehicleStatus.Active
                ? $"{vehicle.VehicleType} ({vehicle.PlateNumber}) is now Active."
                : $"{vehicle.VehicleType} ({vehicle.PlateNumber}) is now Inactive — it won't appear when creating a new ride.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- DELETE -------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == user.Id);
            if (vehicle is null) return NotFound();

            var inUse = await _db.Rides.AnyAsync(r => r.VehicleId == id
                && (r.Status == RideStatus.Active || r.Status == RideStatus.Full));
            if (inUse)
            {
                TempData["Error"] = "This vehicle is being used by an active ride — cancel or complete that ride first.";
                return RedirectToAction(nameof(Index));
            }

            _db.Vehicles.Remove(vehicle);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Vehicle removed.";
            return RedirectToAction(nameof(Index));
        }
    }
}
