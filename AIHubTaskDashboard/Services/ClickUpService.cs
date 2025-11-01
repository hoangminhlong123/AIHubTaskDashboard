using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AIHubTaskDashboard.Services
{
	public class ClickUpService
	{
		private readonly HttpClient _httpClient;
		private readonly string _token;
		private readonly ILogger<ClickUpService> _logger;
		private readonly ApiClientService _apiClient;
		private readonly UserMappingService _userMapping;

		public ClickUpService(
			IConfiguration config,
			ILogger<ClickUpService> logger,
			ApiClientService apiClient,
			UserMappingService userMapping)
		{
			_httpClient = new HttpClient();
			_token = config["ClickUpSettings:Token"] ?? "";
			_logger = logger;
			_apiClient = apiClient;
			_userMapping = userMapping;

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_token);
			_httpClient.DefaultRequestHeaders.Add("User-Agent", "AIHubTaskDashboard");
		}

		// =============================
		// 🎯 MAIN: Xử lý Webhook Events
		// =============================
		public async Task HandleWebhookEventAsync(string eventType, JsonElement payload)
		{
			try
			{
				_logger.LogInformation($"🔄 Processing event: {eventType}");
				_logger.LogInformation($"📦 Full payload: {payload}");

				switch (eventType)
				{
					case "taskCreated":
						// 🔥 DISABLE taskCreated để tránh duplicate
						// Task sẽ được tạo từ Dashboard → ClickUp, không cần sync ngược lại
						_logger.LogInformation("⚠️ [SKIP] taskCreated event ignored to prevent duplicates");
						_logger.LogInformation("💡 Tasks should be created from Dashboard, not ClickUp");
						break;

					case "taskUpdated":
						await HandleTaskUpdated(payload);
						break;
					case "taskDeleted":
						await HandleTaskDeleted(payload);
						break;
					case "taskStatusUpdated":
						await HandleTaskStatusUpdated(payload);
						break;
					case "taskAssigneeUpdated":
						await HandleTaskAssigneeUpdated(payload);
						break;
					default:
						_logger.LogWarning($"⚠️ Unhandled event type: {eventType}");
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error handling {eventType}: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
				throw;
			}
		}


		// =============================
		// 🎯 Map ClickUp Assignees to Dashboard
		// =============================
		private async Task<int> MapClickUpAssigneeToDashboard(JsonElement task)
		{
			try
			{
				_logger.LogInformation("🔍 [MAPPING] Starting assignee mapping...");

				if (!task.TryGetProperty("assignees", out var assigneesArray))
				{
					_logger.LogWarning("⚠️ [MAPPING] No 'assignees' property found, using default assignee");
					return 1;
				}

				if (assigneesArray.GetArrayLength() == 0)
				{
					_logger.LogWarning("⚠️ [MAPPING] Assignees array is empty, using default assignee");
					return 1;
				}

				// Lấy assignee đầu tiên (primary assignee)
				var primaryAssignee = assigneesArray[0];
				var clickUpUserId = GetPropertySafe(primaryAssignee, "id");
				var clickUpUsername = GetPropertySafe(primaryAssignee, "username");
				var clickUpEmail = GetPropertySafe(primaryAssignee, "email");

				_logger.LogInformation($"📋 [MAPPING] ClickUp Assignee Info:");
				_logger.LogInformation($"   - ID: {clickUpUserId}");
				_logger.LogInformation($"   - Username: {clickUpUsername}");
				_logger.LogInformation($"   - Email: {clickUpEmail}");

				if (string.IsNullOrEmpty(clickUpUserId))
				{
					_logger.LogWarning("⚠️ [MAPPING] Invalid ClickUp user ID, using default");
					return 1;
				}

				// Map sang Dashboard user
				var dashboardUserId = await _userMapping.MapClickUpUserToDashboard(clickUpUserId);

				if (dashboardUserId.HasValue)
				{
					_logger.LogInformation($"✅ [MAPPING] Successfully mapped: ClickUp {clickUpUserId} → Dashboard {dashboardUserId.Value}");
					return dashboardUserId.Value;
				}

				_logger.LogWarning($"⚠️ [MAPPING] No mapping found for ClickUp user {clickUpUserId}, using default assignee");
				return 1;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] MapClickUpAssigneeToDashboard error: {ex.Message}");
				_logger.LogError($"❌ [MAPPING] StackTrace: {ex.StackTrace}");
				return 1;
			}
		}

		//// =============================
		//// 📥 Task Created - FIXED với Duplicate Prevention
		//// =============================
		//private async Task HandleTaskCreated(JsonElement payload)
		//{
		//	try
		//	{
		//		_logger.LogInformation("🔍 HandleTaskCreated: Start");

		//		var taskId = GetPropertySafe(payload, "task_id");

		//		if (string.IsNullOrEmpty(taskId))
		//		{
		//			_logger.LogWarning("⚠️ taskCreated: Missing 'task_id'");
		//			return;
		//		}

		//		_logger.LogInformation($"📌 Task ID from webhook: {taskId}");

		//		// 🔥 STEP 1: CHECK IF TASK ALREADY EXISTS (INCLUDING EXACT MATCH)
		//		var existingTaskJson = await TryGetExistingTask(taskId);

		//		if (existingTaskJson != null)
		//		{
		//			_logger.LogWarning($"⚠️ [DUPLICATE PREVENTED] Task already exists in Dashboard: {taskId}");
		//			_logger.LogWarning($"⚠️ This is likely created by TasksController, skipping webhook creation");
		//			return; // 🔥 STOP HERE - Don't create duplicate
		//		}

		//		// 🔥 STEP 2: CHECK FOR PLACEHOLDER TASKS (PENDING_*)
		//		// Webhook might arrive before TasksController updates the placeholder
		//		var placeholderCheck = await CheckForPlaceholderTask(taskId);
		//		if (placeholderCheck != null)
		//		{
		//			_logger.LogWarning($"⚠️ [DUPLICATE PREVENTED] Found placeholder task waiting for this clickup_id");

		//			// Update the placeholder instead of creating new
		//			var placeholderTask = JsonDocument.Parse(placeholderCheck).RootElement;
		//			var dbTaskId = placeholderTask.GetProperty("task_id").GetInt32();

		//			_logger.LogInformation($"🔄 Updating placeholder task {dbTaskId} with webhook data");

		//			// Fetch full task details from ClickUp
		//			var taskDetails = await FetchTaskFromClickUp(taskId);
		//			if (taskDetails == null)
		//			{
		//				_logger.LogError($"❌ Cannot fetch task details from ClickUp: {taskId}");
		//				return;
		//			}

		//			var task = JsonDocument.Parse(taskDetails).RootElement;
		//			var assigneeId = await MapClickUpAssigneeToDashboard(task);

		//			var updatePayload = new
		//			{
		//				clickup_id = taskId, // Replace PENDING_* with real ID
		//				status = MapClickUpStatus(GetNestedPropertySafe(task, "status", "status")),
		//				progress_percentage = CalculateProgress(GetNestedPropertySafe(task, "status", "status")),
		//				assignee_id = assigneeId
		//			};

		//			await _apiClient.PutAsync($"api/v1/tasks/{dbTaskId}", updatePayload);
		//			_logger.LogInformation($"✅ Updated placeholder task with webhook data: {taskId}");
		//			return;
		//		}

		//		// 🔥 STEP 3: If no existing task, create new one (this is a REAL webhook-only creation)
		//		_logger.LogInformation($"✅ No existing task found, creating new from webhook");

		//		var taskDetails2 = await FetchTaskFromClickUp(taskId);

		//		if (taskDetails2 == null)
		//		{
		//			_logger.LogError($"❌ Cannot fetch task details from ClickUp: {taskId}");
		//			return;
		//		}

		//		_logger.LogInformation($"✅ Fetched task details: {taskDetails2}");

		//		var task2 = JsonDocument.Parse(taskDetails2).RootElement;
		//		var taskName = GetPropertySafe(task2, "name");
		//		var status = GetNestedPropertySafe(task2, "status", "status");
		//		var dueDate = GetPropertySafe(task2, "due_date");
		//		var url = GetPropertySafe(task2, "url");
		//		var description = GetPropertySafe(task2, "description");

		//		// 🔥 Map assignee từ ClickUp sang Dashboard
		//		var assigneeId2 = await MapClickUpAssigneeToDashboard(task2);

		//		_logger.LogInformation($"✅ Task created: {taskName} ({taskId}) | Status: {status} | Assignee: {assigneeId2}");

		//		// Prepare payload for Dashboard API
		//		var dashboardPayload = new
		//		{
		//			clickup_id = taskId,
		//			title = taskName,
		//			description = string.IsNullOrEmpty(description) ? $"Synced from ClickUp - Status: {status}" : description,
		//			status = MapClickUpStatus(status),
		//			progress_percentage = CalculateProgress(status),
		//			assignee_id = assigneeId2,
		//			assigner_id = assigneeId2,
		//			collaborators = new List<int> { assigneeId2 },
		//			expected_output = "Auto-synced from ClickUp",
		//			deadline = ParseClickUpDate(dueDate),
		//			notion_link = url
		//		};

		//		_logger.LogInformation($"📤 Sending sync request to Dashboard API with assignee_id={assigneeId2}");
		//		await _apiClient.PostAsync("api/v1/tasks", dashboardPayload);
		//		_logger.LogInformation($"✅ Successfully synced to Dashboard: {taskId}");
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError($"❌ HandleTaskCreated error: {ex.Message}");
		//		_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
		//	}
		//}

		// =============================
		// 🔄 Task Updated - KEEP THIS
		// =============================
		private async Task HandleTaskUpdated(JsonElement payload)
		{
			try
			{
				_logger.LogInformation("🔍 HandleTaskUpdated: Start");

				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskUpdated: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"📌 Task ID from webhook: {taskId}");

				// Check if task exists in Dashboard
				var existingTaskJson = await TryGetExistingTask(taskId);

				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ Task not found in Dashboard: {taskId}");
					_logger.LogWarning($"💡 Task was likely created in ClickUp directly, not syncing");
					return;
				}

				var taskDetails = await FetchTaskFromClickUp(taskId);

				if (taskDetails == null)
				{
					_logger.LogError($"❌ Cannot fetch task details from ClickUp: {taskId}");
					return;
				}

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var taskName = GetPropertySafe(task, "name");
				var status = GetNestedPropertySafe(task, "status", "status");
				var dueDate = GetPropertySafe(task, "due_date");
				var url = GetPropertySafe(task, "url");
				var description = GetPropertySafe(task, "description");

				// 🔥 Map assignee từ ClickUp sang Dashboard
				var assigneeId = await MapClickUpAssigneeToDashboard(task);

				_logger.LogInformation($"🔄 Task updated: {taskName} ({taskId}) | Status: {status} | Assignee: {assigneeId}");

				// Get Dashboard task ID
				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				var dashboardPayload = new
				{
					title = taskName,
					description = string.IsNullOrEmpty(description) ? $"Synced from ClickUp - Status: {status}" : description,
					status = MapClickUpStatus(status),
					progress_percentage = CalculateProgress(status),
					assignee_id = assigneeId,
					deadline = ParseClickUpDate(dueDate),
					notion_link = url
				};

				_logger.LogInformation($"📤 Updating Dashboard (task_id={dbTaskId}, assignee_id={assigneeId})");
				await _apiClient.PutAsync($"api/v1/tasks/{dbTaskId}", dashboardPayload);
				_logger.LogInformation($"✅ Successfully updated in Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskUpdated error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 🗑️ Task Deleted
		// =============================
		private async Task HandleTaskDeleted(JsonElement payload)
		{
			try
			{
				_logger.LogInformation("🔍 HandleTaskDeleted: Start");

				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskDeleted: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"🗑️ Deleting task: {taskId}");

				var existingTaskJson = await TryGetExistingTask(taskId);

				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ Task not found in Dashboard: {taskId}");
					return;
				}

				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				await _apiClient.DeleteAsync($"api/v1/tasks/{dbTaskId}");
				_logger.LogInformation($"✅ Successfully deleted from Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskDeleted error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 📊 Task Status Updated
		// =============================
		private async Task HandleTaskStatusUpdated(JsonElement payload)
		{
			try
			{
				_logger.LogInformation("🔍 HandleTaskStatusUpdated: Start");

				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskStatusUpdated: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"📌 Task ID from webhook: {taskId}");

				// Check if task exists
				var existingTaskJson = await TryGetExistingTask(taskId);

				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ Task not found in Dashboard: {taskId}");
					return;
				}

				// Get new status from history_items
				var newStatus = "";
				if (payload.TryGetProperty("history_items", out var historyItems) && historyItems.GetArrayLength() > 0)
				{
					var lastHistory = historyItems[historyItems.GetArrayLength() - 1];
					if (lastHistory.TryGetProperty("after", out var after))
					{
						newStatus = GetPropertySafe(after, "status");
					}
				}

				if (string.IsNullOrEmpty(newStatus))
				{
					_logger.LogWarning("⚠️ Cannot extract status from history_items, fetching from API");
					var taskDetails = await FetchTaskFromClickUp(taskId);
					if (taskDetails != null)
					{
						var task = JsonDocument.Parse(taskDetails).RootElement;
						newStatus = GetNestedPropertySafe(task, "status", "status");
					}
				}

				_logger.LogInformation($"📊 Task status updated: {taskId} → {newStatus}");

				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				var dashboardPayload = new
				{
					status = MapClickUpStatus(newStatus),
					progress_percentage = CalculateProgress(newStatus)
				};

				_logger.LogInformation($"📤 Sending status update to Dashboard API (task_id={dbTaskId})");
				await _apiClient.PutAsync($"api/v1/tasks/{dbTaskId}", dashboardPayload);
				_logger.LogInformation($"✅ Successfully updated status in Dashboard: {taskId} → {newStatus}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskStatusUpdated error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 👤 Task Assignee Updated
		// =============================
		private async Task HandleTaskAssigneeUpdated(JsonElement payload)
		{
			try
			{
				_logger.LogInformation("🔍 HandleTaskAssigneeUpdated: Start");

				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskAssigneeUpdated: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"👤 Task assignee updated: {taskId}");

				// Re-sync toàn bộ task
				await HandleTaskUpdated(payload);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskAssigneeUpdated error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 🌐 Fetch Task từ ClickUp API
		// =============================
		private async Task<string?> FetchTaskFromClickUp(string taskId)
		{
			try
			{
				_logger.LogInformation($"🌐 Fetching task from ClickUp API: {taskId}");

				var response = await _httpClient.GetAsync($"task/{taskId}");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ ClickUp API failed: {response.StatusCode} - {content}");
					return null;
				}

				_logger.LogInformation($"✅ Successfully fetched task from ClickUp");
				return content;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error fetching task from ClickUp: {ex.Message}");
				return null;
			}
		}

		// =============================
		// 🔥 NEW: Check for Placeholder Tasks (PENDING_*)
		// =============================
		private async Task<string?> CheckForPlaceholderTask(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🔍 [PLACEHOLDER] Checking for placeholder tasks...");

				// Get all recent tasks
				var response = await _apiClient.GetAsync("api/v1/tasks");

				if (string.IsNullOrEmpty(response))
				{
					_logger.LogInformation("⚠️ [PLACEHOLDER] No tasks response from API");
					return null;
				}

				var tasks = JsonDocument.Parse(response).RootElement;

				if (tasks.ValueKind != JsonValueKind.Array)
				{
					_logger.LogWarning("⚠️ [PLACEHOLDER] Response is not an array");
					return null;
				}

				// Look for tasks created in last 30 seconds with PENDING_ clickup_id
				var now = DateTime.UtcNow;

				foreach (var task in tasks.EnumerateArray())
				{
					if (!task.TryGetProperty("clickup_id", out var existingClickupId))
						continue;

					var existingId = existingClickupId.GetString();

					// Check if this is a PENDING placeholder
					if (string.IsNullOrEmpty(existingId) || !existingId.StartsWith("PENDING_"))
						continue;

					// Check if created recently (within 30 seconds)
					if (task.TryGetProperty("created_at", out var createdAtProp))
					{
						var createdAtStr = createdAtProp.GetString();
						if (DateTime.TryParse(createdAtStr, out var createdAt))
						{
							var age = (now - createdAt).TotalSeconds;

							if (age < 30) // Only consider recent placeholders
							{
								_logger.LogInformation($"✅ [PLACEHOLDER] Found placeholder task: {existingId} (age: {age:F1}s)");
								return task.ToString();
							}
						}
					}
				}

				_logger.LogInformation("⚠️ [PLACEHOLDER] No recent placeholder task found");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [PLACEHOLDER] CheckForPlaceholderTask error: {ex.Message}");
				_logger.LogError($"❌ [PLACEHOLDER] StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// =============================
		// 🔥 IMPROVED: Get Existing Task by ClickUp ID
		// =============================
		private async Task<string?> TryGetExistingTask(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🔍 Checking for existing task: clickup_id={clickupId}");

				// Try direct query first
				var response = await _apiClient.GetAsync($"api/v1/tasks?clickup_id={clickupId}");

				if (!string.IsNullOrEmpty(response))
				{
					var result = JsonDocument.Parse(response).RootElement;

					if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
					{
						_logger.LogInformation($"✅ Found existing task via query");
						return result[0].ToString();
					}

					if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("task_id", out _))
					{
						_logger.LogInformation($"✅ Found existing task (single object)");
						return response;
					}
				}

				// If query fails, try fetching all recent tasks and search manually
				_logger.LogInformation($"🔄 Query returned no results, searching all tasks...");

				var allTasksResponse = await _apiClient.GetAsync("api/v1/tasks");

				if (!string.IsNullOrEmpty(allTasksResponse))
				{
					var allTasks = JsonDocument.Parse(allTasksResponse).RootElement;

					if (allTasks.ValueKind == JsonValueKind.Array)
					{
						foreach (var task in allTasks.EnumerateArray())
						{
							if (task.TryGetProperty("clickup_id", out var existingClickupId))
							{
								var existingId = existingClickupId.GetString();

								if (!string.IsNullOrEmpty(existingId) && existingId == clickupId)
								{
									_logger.LogInformation($"✅ Found existing task via manual search");
									return task.ToString();
								}
							}
						}
					}
				}

				_logger.LogInformation("⚠️ No existing task found");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ TryGetExistingTask error: {ex.Message}");
				return null;
			}
		}

		// =============================
		// 🛠️ Helper Methods
		// =============================
		private string GetPropertySafe(JsonElement element, string propertyName)
		{
			try
			{
				if (element.TryGetProperty(propertyName, out var prop))
				{
					if (prop.ValueKind == JsonValueKind.Null)
					{
						return "";
					}
					return prop.GetString() ?? "";
				}
				return "";
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"⚠️ GetPropertySafe failed for {propertyName}: {ex.Message}");
				return "";
			}
		}

		private string GetNestedPropertySafe(JsonElement element, string parent, string child)
		{
			try
			{
				if (element.TryGetProperty(parent, out var parentProp))
				{
					if (parentProp.ValueKind == JsonValueKind.Null)
					{
						_logger.LogInformation($"⚠️ Property '{parent}' is null");
						return "";
					}

					if (parentProp.ValueKind == JsonValueKind.Object &&
						parentProp.TryGetProperty(child, out var childProp))
					{
						if (childProp.ValueKind == JsonValueKind.Null)
						{
							return "";
						}
						return childProp.GetString() ?? "";
					}
				}

				return "";
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"⚠️ GetNestedPropertySafe failed for {parent}.{child}: {ex.Message}");
				return "";
			}
		}

		private string MapClickUpStatus(string clickUpStatus)
		{
			return clickUpStatus?.ToLower() switch
			{
				"to do" => "To Do",
				"in progress" => "In Progress",
				"complete" => "Completed",
				"closed" => "Completed",
				"review" => "In Progress",
				_ => "To Do"
			};
		}

		private int CalculateProgress(string status)
		{
			return status?.ToLower() switch
			{
				"to do" => 0,
				"in progress" => 50,
				"review" => 75,
				"complete" => 100,
				"closed" => 100,
				_ => 0
			};
		}

		private string ParseClickUpDate(string? dueDate)
		{
			if (string.IsNullOrEmpty(dueDate))
				return DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

			if (long.TryParse(dueDate, out long timestamp))
			{
				var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
				return date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
			}

			return DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
		}
		private async Task<string?> TryGetExistingTaskWithRetry(string clickupId, int maxRetries = 3)
		{
			for (int attempt = 1; attempt <= maxRetries; attempt++)
			{
				_logger.LogInformation($"🔍 [RETRY {attempt}/{maxRetries}] Checking for existing task: {clickupId}");

				var existingTask = await TryGetExistingTask(clickupId);

				if (existingTask != null)
				{
					_logger.LogInformation($"✅ [RETRY {attempt}] Found existing task!");
					return existingTask;
				}

				if (attempt < maxRetries)
				{
					var delayMs = attempt * 500; // 500ms, 1000ms, 1500ms
					_logger.LogInformation($"⏳ [RETRY {attempt}] Not found, waiting {delayMs}ms before retry...");
					await Task.Delay(delayMs);
				}
			}

			_logger.LogInformation($"⚠️ [RETRY] Task not found after {maxRetries} attempts");
			return null;
		}
	}
}