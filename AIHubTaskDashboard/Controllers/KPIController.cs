using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	public class KPIController : Controller
	{
		private readonly ApiClientService _api;
		private readonly ILogger<KPIController> _logger;

		public KPIController(ApiClientService api, ILogger<KPIController> logger)
		{
			_api = api;
			_logger = logger;
		}

		public async Task<IActionResult> Index(string? team)
		{
			try
			{
				// Mặc định là Dev nếu không có team được chọn
				if (string.IsNullOrEmpty(team))
				{
					team = "Dev";
				}

				ViewBag.CurrentTeam = team;

				// Lấy danh sách users theo team
				var usersRes = await _api.GetAsync($"api/v1/users?team={team}");
				var users = JsonDocument.Parse(usersRes).RootElement;
				ViewBag.Users = users;

				// Lấy KPI data cho team
				var kpiRes = await _api.GetAsync($"api/v1/kpi?team={team}");
				var kpiData = JsonDocument.Parse(kpiRes).RootElement;

				return View(kpiData);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ KPI Error: {ex.Message}");
				var emptyJson = JsonDocument.Parse("{}").RootElement;
				ViewBag.Users = JsonDocument.Parse("[]").RootElement;
				ViewBag.CurrentTeam = team ?? "Dev";
				return View(emptyJson);
			}
		}

		[HttpGet]
		public async Task<IActionResult> GetTeamKPI(string team)
		{
			try
			{
				var kpiRes = await _api.GetAsync($"api/v1/kpi?team={team}");
				return Content(kpiRes, "application/json");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ GetTeamKPI Error: {ex.Message}");
				return StatusCode(500, new { error = ex.Message });
			}
		}
	}
}