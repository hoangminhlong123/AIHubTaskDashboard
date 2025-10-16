using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
