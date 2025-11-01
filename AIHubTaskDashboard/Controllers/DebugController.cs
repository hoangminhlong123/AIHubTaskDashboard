using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Controllers
{
	[ApiController]
	[Route("api/debug")]
	[AllowAnonymous]

	public class DebugController : ControllerBase
	{
		private readonly UserMappingService _userMapping;
		private readonly ILogger<DebugController> _logger;

		public DebugController(
			UserMappingService userMapping,
			ILogger<DebugController> logger)
		{
			_userMapping = userMapping;
			_logger = logger;
		}

		/// <summary>
		/// Xem toàn bộ user mapping hiện tại
		/// GET /api/debug/user-mapping
		/// </summary>
		[HttpGet("user-mapping")]
		public async Task<IActionResult> GetUserMapping()
		{
			try
			{
				_logger.LogInformation("📊 [DEBUG] Getting user mapping report");
				var report = await _userMapping.GetMappingReport();
				return Ok(new
				{
					success = true,
					timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
					data = report
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] GetUserMapping error: {ex.Message}");
				return StatusCode(500, new { success = false, error = ex.Message });
			}
		}

		/// <summary>
		/// Test mapping từ ClickUp sang Dashboard
		/// GET /api/debug/map-to-dashboard/{clickUpUserId}
		/// </summary>
		[HttpGet("map-to-dashboard/{clickUpUserId}")]
		public async Task<IActionResult> MapToDashboard(string clickUpUserId)
		{
			try
			{
				_logger.LogInformation($"🔍 [DEBUG] Testing ClickUp → Dashboard mapping for: {clickUpUserId}");
				var dashboardUserId = await _userMapping.MapClickUpUserToDashboard(clickUpUserId);

				return Ok(new
				{
					success = true,
					clickup_user_id = clickUpUserId,
					dashboard_user_id = dashboardUserId,
					mapped = dashboardUserId.HasValue,
					message = dashboardUserId.HasValue
						? $"Successfully mapped to Dashboard user {dashboardUserId.Value}"
						: "No mapping found",
					timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] MapToDashboard error: {ex.Message}");
				return StatusCode(500, new { success = false, error = ex.Message });
			}
		}

		/// <summary>
		/// Test mapping từ Dashboard sang ClickUp
		/// GET /api/debug/map-to-clickup/{dashboardUserId}
		/// </summary>
		[HttpGet("map-to-clickup/{dashboardUserId}")]
		public async Task<IActionResult> MapToClickUp(int dashboardUserId)
		{
			try
			{
				_logger.LogInformation($"🔍 [DEBUG] Testing Dashboard → ClickUp mapping for: {dashboardUserId}");
				var clickUpUserId = await _userMapping.MapDashboardUserToClickUp(dashboardUserId);

				return Ok(new
				{
					success = true,
					dashboard_user_id = dashboardUserId,
					clickup_user_id = clickUpUserId,
					mapped = !string.IsNullOrEmpty(clickUpUserId),
					message = !string.IsNullOrEmpty(clickUpUserId)
						? $"Successfully mapped to ClickUp user {clickUpUserId}"
						: "No mapping found",
					timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] MapToClickUp error: {ex.Message}");
				return StatusCode(500, new { success = false, error = ex.Message });
			}
		}

		/// <summary>
		/// Clear cache và rebuild mapping
		/// POST /api/debug/refresh-mapping
		/// </summary>
		[HttpPost("refresh-mapping")]
		public async Task<IActionResult> RefreshMapping()
		{
			try
			{
				_logger.LogInformation("🔄 [DEBUG] Refreshing user mapping cache");
				_userMapping.ClearCache();
				var report = await _userMapping.GetMappingReport();

				return Ok(new
				{
					success = true,
					message = "Mapping refreshed successfully",
					timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
					data = report
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] RefreshMapping error: {ex.Message}");
				return StatusCode(500, new { success = false, error = ex.Message });
			}
		}

		/// <summary>
		/// Health check cho mapping service
		/// GET /api/debug/health
		/// </summary>
		[HttpGet("health")]
		public IActionResult Health()
		{
			return Ok(new
			{
				success = true,
				service = "User Mapping Debug Service",
				status = "healthy",
				timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
			});
		}
	}
}