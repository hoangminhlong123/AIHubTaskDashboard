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

		// =============================
		// 🔍 DEBUG ENDPOINTS (THÊM MỚI)
		// =============================

		/// <summary>
		/// 🔍 DEBUG: Test kết nối ClickUp API với task thực
		/// </summary>
		[HttpGet("debug/test-connection")]
		public async Task<IActionResult> TestConnection([FromServices] ApiClientService apiClient)
		{
			try
			{
				var testTaskId = Request.Query["task_id"].ToString();
				if (string.IsNullOrEmpty(testTaskId))
				{
					return BadRequest(new
					{
						error = "Provide ?task_id=YOUR_TASK_ID",
						example = "/api/clickup-webhook/debug/test-connection?task_id=86c4b4ych"
					});
				}

				_logger.LogInformation($"🧪 [DEBUG] Testing connection with task: {testTaskId}");

				// Test bằng cách gọi trực tiếp ClickUp API
				using var httpClient = new HttpClient();
				var token = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["ClickUpSettings:Token"];
				var baseUrl = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["ClickUpSettings:ApiBaseUrl"]
					?? "https://api.clickup.com/api/v2/";

				httpClient.BaseAddress = new Uri(baseUrl);
				httpClient.DefaultRequestHeaders.Add("Authorization", token);

				_logger.LogInformation($"🌐 [DEBUG] Calling: {baseUrl}task/{testTaskId}");
				_logger.LogInformation($"🔑 [DEBUG] Token: {token?.Substring(0, Math.Min(15, token?.Length ?? 0))}...");

				var response = await httpClient.GetAsync($"task/{testTaskId}");
				var content = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📡 [DEBUG] Response: {response.StatusCode}");
				_logger.LogInformation($"📥 [DEBUG] Content length: {content?.Length ?? 0}");

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [DEBUG] Failed: {content}");
					return StatusCode((int)response.StatusCode, new
					{
						success = false,
						status = response.StatusCode.ToString(),
						error = content,
						hint = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
							? "Token invalid hoặc không có quyền"
							: "Task không tồn tại hoặc lỗi khác"
					});
				}

				var taskData = JsonDocument.Parse(content).RootElement;
				return Ok(new
				{
					success = true,
					message = "✅ Kết nối ClickUp thành công!",
					task_id = testTaskId,
					task_name = taskData.TryGetProperty("name", out var name) ? name.GetString() : "N/A",
					status = taskData.TryGetProperty("status", out var status)
						? status.TryGetProperty("status", out var s) ? s.GetString() : "N/A"
						: "N/A",
					data_preview = content.Substring(0, Math.Min(500, content.Length))
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] Exception: {ex.Message}");
				return StatusCode(500, new
				{
					error = ex.Message,
					type = ex.GetType().Name,
					stackTrace = ex.StackTrace
				});
			}
		}

		/// <summary>
		/// 🔍 DEBUG: Tìm task trong Dashboard
		/// </summary>
		[HttpGet("debug/find-task")]
		public async Task<IActionResult> FindTask([FromServices] ApiClientService apiClient)
		{
			try
			{
				var clickupId = Request.Query["clickup_id"].ToString();
				if (string.IsNullOrEmpty(clickupId))
				{
					return BadRequest(new
					{
						error = "Provide ?clickup_id=YOUR_CLICKUP_ID",
						example = "/api/clickup-webhook/debug/find-task?clickup_id=86c4b4ych"
					});
				}

				_logger.LogInformation($"🔍 [DEBUG] Searching for clickup_id: {clickupId}");

				// Method 1: Query trực tiếp
				_logger.LogInformation($"📡 [DEBUG] Method 1: Query by clickup_id");
				string? queryResult = null;
				try
				{
					queryResult = await apiClient.GetAsync($"api/v1/tasks?clickup_id={clickupId}");
					_logger.LogInformation($"✅ [DEBUG] Query response length: {queryResult?.Length ?? 0}");
				}
				catch (Exception ex)
				{
					_logger.LogWarning($"⚠️ [DEBUG] Query failed: {ex.Message}");
				}

				// Method 2: Tìm trong tất cả tasks
				_logger.LogInformation($"📡 [DEBUG] Method 2: Search in all tasks");
				JsonElement? foundTask = null;
				int totalTasks = 0;

				try
				{
					var allTasksResult = await apiClient.GetAsync("api/v1/tasks");
					var allTasks = JsonDocument.Parse(allTasksResult).RootElement;

					if (allTasks.ValueKind == JsonValueKind.Array)
					{
						totalTasks = allTasks.GetArrayLength();
						_logger.LogInformation($"📊 [DEBUG] Total tasks in Dashboard: {totalTasks}");

						foreach (var task in allTasks.EnumerateArray())
						{
							if (task.TryGetProperty("clickup_id", out var cid) &&
								cid.GetString() == clickupId)
							{
								foundTask = task;
								_logger.LogInformation($"✅ [DEBUG] Found task in array!");
								break;
							}
						}
					}
				}
				catch (Exception ex)
				{
					_logger.LogError($"❌ [DEBUG] Search failed: {ex.Message}");
				}

				return Ok(new
				{
					clickup_id = clickupId,
					found_by_query = !string.IsNullOrEmpty(queryResult),
					found_in_all_tasks = foundTask.HasValue,
					total_tasks_in_dashboard = totalTasks,
					query_response = queryResult,
					task_data = foundTask,
					conclusion = foundTask.HasValue
						? "✅ Task đã tồn tại trong Dashboard"
						: "❌ Task CHƯA có trong Dashboard (cần create mới)"
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] Error: {ex.Message}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		/// <summary>
		/// 🔍 DEBUG: Kiểm tra user mapping
		/// </summary>
		[HttpGet("debug/user-mapping")]
		public async Task<IActionResult> TestUserMapping([FromServices] UserMappingService userMapping)
		{
			try
			{
				_logger.LogInformation("🔍 [DEBUG] Testing user mapping");
				var report = await userMapping.GetMappingReport();

				return Ok(new
				{
					success = true,
					message = "✅ User mapping report",
					data = report
				});
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DEBUG] User mapping error: {ex.Message}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		/// <summary>
		/// 🔍 DEBUG: Simulate tạo task từ ClickUp với task ID thực
		/// </summary>
		[HttpPost("debug/simulate-create")]
		public async Task<IActionResult> SimulateCreate()
		{
			var taskId = Request.Query["task_id"].ToString();
			if (string.IsNullOrEmpty(taskId))
			{
				return BadRequest(new
				{
					error = "Provide ?task_id=YOUR_TASK_ID",
					example = "/api/clickup-webhook/debug/simulate-create?task_id=86c4b4ych"
				});
			}

			_logger.LogInformation($"🧪 [DEBUG] Simulating taskCreated event for: {taskId}");

			var fakePayload = JsonDocument.Parse($@"{{
				""event"": ""taskCreated"",
				""task_id"": ""{taskId}"",
				""history_items"": [
					{{
						""id"": ""debug_{Guid.NewGuid()}"",
						""type"": 1,
						""date"": ""{DateTime.UtcNow:o}"",
						""field"": ""status"",
						""parent_id"": ""{taskId}"",
						""data"": {{}},
						""user"": {{
							""id"": 123456,
							""username"": ""Debug User""
						}}
					}}
				],
				""webhook_id"": ""debug-webhook-{DateTime.UtcNow.Ticks}""
			}}").RootElement;

			return await HandleWebhook(fakePayload);
		}

		/// <summary>
		/// 🔍 DEBUG: Tổng hợp thông tin webhook
		/// </summary>
		[HttpGet("debug/info")]
		public IActionResult WebhookInfo()
		{
			var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
			var token = config["ClickUpSettings:Token"];
			var baseUrl = config["ClickUpSettings:ApiBaseUrl"];
			var listId = config["ClickUpSettings:ListId"];
			var teamId = config["ClickUpSettings:TeamId"];

			return Ok(new
			{
				message = "🔍 ClickUp Webhook Debug Info",
				webhook_endpoint = "/api/clickup-webhook",
				status = "✅ Active",
				configuration = new
				{
					token_configured = !string.IsNullOrEmpty(token),
					token_preview = token?.Substring(0, Math.Min(15, token?.Length ?? 0)) + "...",
					token_length = token?.Length ?? 0,
					base_url = baseUrl ?? "https://api.clickup.com/api/v2/",
					list_id = listId,
					team_id = teamId
				},
				debug_endpoints = new
				{
					test_connection = "/api/clickup-webhook/debug/test-connection?task_id=YOUR_ID",
					find_task = "/api/clickup-webhook/debug/find-task?clickup_id=YOUR_ID",
					user_mapping = "/api/clickup-webhook/debug/user-mapping",
					simulate_create = "/api/clickup-webhook/debug/simulate-create?task_id=YOUR_ID",
					info = "/api/clickup-webhook/debug/info"
				},
				request_info = new
				{
					remote_ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
					timestamp = DateTime.UtcNow
				},
				tips = new[]
				{
					"1️⃣ Test connection trước để đảm bảo token hợp lệ",
					"2️⃣ Dùng simulate-create để test flow tạo task",
					"3️⃣ Check logs trong console để debug chi tiết",
					"4️⃣ Đảm bảo webhook URL trong ClickUp là HTTPS"
				}
			});
		}
		/// <summary>
		/// 🔍 DEBUG: Lấy danh sách tasks từ list để test
		/// </summary>
		[HttpGet("debug/get-tasks-from-list")]
		public async Task<IActionResult> GetTasksFromList([FromServices] IConfiguration config)
		{
			try
			{
				var listId = config["ClickUpSettings:ListId"];
				var token = config["ClickUpSettings:Token"];

				_logger.LogInformation($"🔍 [DEBUG] Fetching tasks from list: {listId}");

				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.Add("Authorization", token);

				var response = await httpClient.GetAsync($"https://api.clickup.com/api/v2/list/{listId}/task");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					return StatusCode((int)response.StatusCode, new
					{
						error = "Failed to fetch tasks",
						status = response.StatusCode,
						details = content
					});
				}

				var tasks = JsonDocument.Parse(content);
				var taskList = new List<object>();

				if (tasks.RootElement.TryGetProperty("tasks", out var tasksArray))
				{
					foreach (var task in tasksArray.EnumerateArray())
					{
						taskList.Add(new
						{
							id = task.GetProperty("id").GetString(),
							name = task.GetProperty("name").GetString(),
							status = task.TryGetProperty("status", out var s)
								? s.TryGetProperty("status", out var st) ? st.GetString() : "N/A"
								: "N/A"
						});
					}
				}

				return Ok(new
				{
					success = true,
					list_id = listId,
					total_tasks = taskList.Count,
					tasks = taskList.Take(10), // Chỉ lấy 10 tasks đầu
					message = "Dùng một trong các task ID này để test simulate-create"
				});
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { error = ex.Message });
			}
		}

		/// <summary>
		/// 🔍 DEBUG: Test xem token có valid không
		/// </summary>
		[HttpGet("debug/test-token")]
		public async Task<IActionResult> TestToken([FromServices] IConfiguration config)
		{
			try
			{
				var token = config["ClickUpSettings:Token"];

				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.Add("Authorization", token);

				// Test với endpoint đơn giản nhất: lấy thông tin user
				var response = await httpClient.GetAsync("https://api.clickup.com/api/v2/user");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					return StatusCode((int)response.StatusCode, new
					{
						success = false,
						message = "Token KHÔNG hợp lệ hoặc đã bị revoke",
						status = response.StatusCode,
						error = content,
						action = "Vào https://app.clickup.com/settings/apps để tạo token mới"
					});
				}

				var user = JsonDocument.Parse(content).RootElement.GetProperty("user");

				return Ok(new
				{
					success = true,
					message = "✅ Token hợp lệ!",
					user = new
					{
						id = user.GetProperty("id").GetInt64(),
						username = user.GetProperty("username").GetString(),
						email = user.GetProperty("email").GetString()
					}
				});
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { error = ex.Message });
			}
		}
	}
}