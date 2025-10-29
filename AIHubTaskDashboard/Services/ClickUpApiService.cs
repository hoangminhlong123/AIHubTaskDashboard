using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Services
{
	public class ClickUpApiService : Controller
	{
		public IActionResult Index()
		{
			return View();
		}
	}
}
