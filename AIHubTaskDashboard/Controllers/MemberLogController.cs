using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Controllers
{
    public class MemberLogController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
