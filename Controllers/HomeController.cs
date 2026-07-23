using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            // Staff/admin/company accounts don't use the student-facing app —
            // send them straight to their portal instead of the student home page.
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin"))
                    return RedirectToAction("Index", "AdminUniversities");
                if (User.IsInRole("UniversityAdmin"))
                    return RedirectToAction("Index", "AdminUniversities");
                if (User.IsInRole("DepartmentStaff"))
                    return RedirectToAction("Index", "StaffTickets");
                if (User.IsInRole("Company"))
                    return RedirectToAction("Index", "Company");
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // Shown when RequireServiceAttribute blocks access because the current
        // user's university hasn't enabled that service (or it isn't built yet).
        public async Task<IActionResult> NotAvailable(string? service)
        {
            Service? serviceInfo = null;
            if (!string.IsNullOrWhiteSpace(service))
                serviceInfo = await _db.Services.FindAsync(service);

            ViewBag.ServiceInfo = serviceInfo;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
