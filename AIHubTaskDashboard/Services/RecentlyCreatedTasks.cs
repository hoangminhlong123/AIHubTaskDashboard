using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Services
{
	public class RecentlyCreatedTasks : Controller
	{
		public IActionResult Index()
		{
			return View();
		}
	}
}
