using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	[ApiController]
	[Route("api/clickup")]
	public class ClickUpWebhookController : ControllerBase
	{
		private readonly ClickUpService _clickUpService;
		private readonly ILogger<ClickUpWebhookController> _logger;

		public ClickUpWebhookController(ClickUpService clickUpService, ILogger<ClickUpWebhookController> logger)
		{
			_clickUpService = clickUpService;
			_logger = logger;
		}

		/// <summary>
		/// Webhook endpoint để nhận events từ ClickUp
		/// </summary>
		[HttpPost("webhook")]
		public async Task<IActionResult> Webhook([FromBody] JsonElement payload)
		{
			try
			{
				_logger.LogInformation($"📥 Webhook received: {payload}");

				// Validate payload
				if (!payload.TryGetProperty("event", out var eventTypeElement))
				{
					_logger.LogWarning("⚠️ Invalid payload: missing 'event' property");
					return BadRequest(new { error = "Invalid payload format: missing 'event'" });
				}

				var eventType = eventTypeElement.GetString();

				if (string.IsNullOrEmpty(eventType))
				{
					_logger.LogWarning("⚠️ Invalid payload: empty event type");
					return BadRequest(new { error = "Invalid payload format: empty event type" });
				}

				// Process webhook
				await _clickUpService.HandleWebhookEventAsync(eventType, payload);

				return Ok(new
				{
					success = true,
					message = "Webhook processed successfully",
					eventType,
					timestamp = DateTime.UtcNow
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Webhook error: {ex.Message}\n{ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		
		[HttpGet("test")]
		public IActionResult Test()
		{
			return Ok(new
			{
				message = "ClickUp webhook endpoint is working!",
				timestamp = DateTime.UtcNow
			});
		}

		
		[HttpPost("sync")]
		public async Task<IActionResult> ManualSync([FromQuery] string listId)
		{
			try
			{
				if (string.IsNullOrEmpty(listId))
					return BadRequest(new { error = "listId is required" });

				var tasks = await _clickUpService.GetTasksAsync(listId);

				return Ok(new
				{
					success = true,
					message = "Sync completed",
					data = tasks
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Manual sync error: {ex.Message}");
				return StatusCode(500, new { error = ex.Message });
			}
		}
	}
}