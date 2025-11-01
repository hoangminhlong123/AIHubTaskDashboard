using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	[ApiController]
	[Route("api/clickup-webhook")]
	public class ClickUpWebHookController : ControllerBase
	{
		private readonly ILogger<ClickUpWebHookController> _logger;
		private readonly ClickUpService _clickUpService;

		public ClickUpWebHookController(
			ILogger<ClickUpWebHookController> logger,
			ClickUpService clickUpService)
		{
			_logger = logger;
			_clickUpService = clickUpService;
		}

		/// <summary>
		/// Nhận webhook từ ClickUp
		/// </summary>
		[HttpPost]
		public async Task<IActionResult> HandleWebhook([FromBody] JsonElement payload)
		{
			try
			{
				_logger.LogInformation($"📩 Webhook received at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
				_logger.LogInformation($"📩 Payload: {payload}");

				if (!payload.TryGetProperty("event", out var eventProp))
				{
					_logger.LogWarning("⚠️ No 'event' property in webhook payload");
					return Ok(new { success = false, message = "No event property" });
				}

				var eventType = eventProp.GetString();
				_logger.LogInformation($"📩 Event type: {eventType}");

				await _clickUpService.HandleWebhookEventAsync(eventType, payload);

				return Ok(new { success = true, message = "Webhook processed", eventType });
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Webhook error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		/// <summary>
		/// Endpoint kiểm tra hoạt động webhook
		/// </summary>
		[HttpGet("test")]
		public IActionResult Test()
		{
			_logger.LogInformation("✅ Test endpoint called");
			return Ok(new
			{
				message = "ClickUp Webhook endpoint is working!",
				timestamp = DateTime.UtcNow,
				endpoint = "/api/clickup-webhook"
			});
		}

		/// <summary>
		/// Health check endpoint
		/// </summary>
		[HttpGet("health")]
		public IActionResult Health()
		{
			return Ok(new
			{
				status = "healthy",
				service = "ClickUp Webhook Service",
				timestamp = DateTime.UtcNow
			});
		}
	}
}
