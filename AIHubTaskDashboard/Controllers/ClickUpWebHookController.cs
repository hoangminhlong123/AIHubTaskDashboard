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
				_logger.LogInformation("🔔 ==========================================");
				_logger.LogInformation($"🔔 [WEBHOOK] RECEIVED at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
				_logger.LogInformation("🔔 ==========================================");
				_logger.LogInformation($"📦 [WEBHOOK] Full payload:\n{JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })}");
				_logger.LogInformation($"🌐 [WEBHOOK] Request from: {HttpContext.Connection.RemoteIpAddress}");
				_logger.LogInformation($"📋 [WEBHOOK] Headers:");
				foreach (var header in HttpContext.Request.Headers)
				{
					_logger.LogInformation($"   - {header.Key}: {header.Value}");
				}

				if (!payload.TryGetProperty("event", out var eventProp))
				{
					_logger.LogWarning("⚠️ [WEBHOOK] No 'event' property in payload");
					return Ok(new { success = false, message = "No event property" });
				}

				var eventType = eventProp.GetString();
				_logger.LogInformation($"📩 [WEBHOOK] Event type: {eventType}");

				// Extract task_id if available
				string? taskId = null;
				if (payload.TryGetProperty("task_id", out var taskIdProp))
				{
					taskId = taskIdProp.GetString();
					_logger.LogInformation($"📌 [WEBHOOK] Task ID: {taskId}");
				}

				// Process webhook asynchronously
				_ = Task.Run(async () =>
				{
					try
					{
						_logger.LogInformation($"🔄 [WEBHOOK] Starting background processing for: {eventType}");
						await _clickUpService.HandleWebhookEventAsync(eventType!, payload);
						_logger.LogInformation($"✅ [WEBHOOK] Successfully processed: {eventType}");
					}
					catch (Exception ex)
					{
						_logger.LogError($"❌ [WEBHOOK] Background processing error: {ex.Message}");
						_logger.LogError($"❌ [WEBHOOK] StackTrace: {ex.StackTrace}");
					}
				});

				// Return 200 OK immediately
				_logger.LogInformation($"✅ [WEBHOOK] Acknowledged webhook (processing in background)");
				return Ok(new
				{
					success = true,
					message = "Webhook received and processing",
					eventType,
					taskId,
					timestamp = DateTime.UtcNow
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [WEBHOOK] Error: {ex.Message}");
				_logger.LogError($"❌ [WEBHOOK] StackTrace: {ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		/// <summary>
		/// Endpoint kiểm tra hoạt động webhook
		/// </summary>
		[HttpGet("test")]
		public IActionResult Test()
		{
			_logger.LogInformation("✅ [WEBHOOK] Test endpoint called");
			return Ok(new
			{
				message = "ClickUp Webhook endpoint is working!",
				timestamp = DateTime.UtcNow,
				endpoint = "/api/clickup-webhook",
				methods = new[] { "POST", "GET" },
				status = "healthy"
			});
		}

		/// <summary>
		/// Health check endpoint
		/// </summary>
		[HttpGet("health")]
		public IActionResult Health()
		{
			_logger.LogInformation("✅ [WEBHOOK] Health check called");
			return Ok(new
			{
				status = "healthy",
				service = "ClickUp Webhook Service",
				timestamp = DateTime.UtcNow,
				uptime = DateTime.UtcNow
			});
		}

		/// <summary>
		/// Test tạo fake webhook (for debugging)
		/// </summary>
		[HttpPost("test-create")]
		public async Task<IActionResult> TestCreate()
		{
			_logger.LogInformation("🧪 [WEBHOOK] Test create task webhook called");

			var fakePayload = JsonDocument.Parse(@"{
				""event"": ""taskCreated"",
				""task_id"": ""test123abc"",
				""history_items"": [
					{
						""id"": ""123"",
						""type"": 1,
						""date"": """ + DateTime.UtcNow.ToString("o") + @""",
						""field"": ""status"",
						""parent_id"": ""test123abc"",
						""data"": {},
						""source"": null,
						""user"": {
							""id"": 123456,
							""username"": ""Test User"",
							""email"": ""test@example.com"",
							""color"": ""#FF0000"",
							""initials"": ""TU"",
							""profilePicture"": null
						},
						""before"": null,
						""after"": null
					}
				],
				""webhook_id"": ""test-webhook""
			}").RootElement;

			return await HandleWebhook(fakePayload);
		}
	}
}