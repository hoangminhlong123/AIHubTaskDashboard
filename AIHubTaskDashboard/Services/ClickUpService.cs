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

		public ClickUpService(
			IConfiguration config,
			ILogger<ClickUpService> logger,
			ApiClientService apiClient)
		{
			_httpClient = new HttpClient();
			_token = config["ClickUpSettings:Token"] ?? "";
			_logger = logger;
			_apiClient = apiClient;

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
						await HandleTaskCreated(payload);
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
		// 📥 Task Created - ✅ FIXED: Lấy task từ ClickUp API
		// =============================
		private async Task HandleTaskCreated(JsonElement payload)
		{
			try
			{
				_logger.LogInformation("🔍 HandleTaskCreated: Start");

				// ClickUp webhook chỉ gửi task_id, không có full task object
				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskCreated: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"📌 Task ID from webhook: {taskId}");

				// Gọi ClickUp API để lấy full task details
				var taskDetails = await FetchTaskFromClickUp(taskId);

				if (taskDetails == null)
				{
					_logger.LogError($"❌ Cannot fetch task details from ClickUp: {taskId}");
					return;
				}

				_logger.LogInformation($"✅ Fetched task details: {taskDetails}");

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var taskName = GetPropertySafe(task, "name");
				var status = GetNestedPropertySafe(task, "status", "status");
				var priority = GetNestedPropertySafe(task, "priority", "priority");
				var dueDate = GetPropertySafe(task, "due_date");
				var url = GetPropertySafe(task, "url");

				var assignees = new List<string>();
				if (task.TryGetProperty("assignees", out var assigneesArray))
				{
					foreach (var assignee in assigneesArray.EnumerateArray())
					{
						var username = GetPropertySafe(assignee, "username");
						if (!string.IsNullOrEmpty(username))
							assignees.Add(username);
					}
				}

				_logger.LogInformation($"✅ Task created: {taskName} ({taskId}) | Status: {status} | Assignees: {string.Join(", ", assignees)}");

				// Sync to Dashboard
				var dto = new
				{
					TaskId = taskId,
					Name = taskName,
					Status = status,
					Priority = priority ?? "normal",
					DueDate = dueDate,
					Url = url,
					Assignees = assignees
				};

				_logger.LogInformation($"📤 Sending sync request to Dashboard API");
				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
				_logger.LogInformation($"✅ Successfully synced to Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskCreated error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 🔄 Task Updated - ✅ FIXED
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

				var taskDetails = await FetchTaskFromClickUp(taskId);

				if (taskDetails == null)
				{
					_logger.LogError($"❌ Cannot fetch task details from ClickUp: {taskId}");
					return;
				}

				_logger.LogInformation($"✅ Fetched task details: {taskDetails}");

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var taskName = GetPropertySafe(task, "name");
				var status = GetNestedPropertySafe(task, "status", "status");
				var priority = GetNestedPropertySafe(task, "priority", "priority");
				var dueDate = GetPropertySafe(task, "due_date");
				var url = GetPropertySafe(task, "url");

				var assignees = new List<string>();
				if (task.TryGetProperty("assignees", out var assigneesArray))
				{
					foreach (var assignee in assigneesArray.EnumerateArray())
					{
						var username = GetPropertySafe(assignee, "username");
						if (!string.IsNullOrEmpty(username))
							assignees.Add(username);
					}
				}

				_logger.LogInformation($"🔄 Task updated: {taskName} ({taskId}) | Status: {status}");

				var dto = new
				{
					TaskId = taskId,
					Name = taskName,
					Status = status,
					Priority = priority ?? "normal",
					DueDate = dueDate,
					Url = url,
					Assignees = assignees
				};

				_logger.LogInformation($"📤 Sending update to Dashboard API");
				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
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

				await _apiClient.DeleteAsync($"api/tasks-sync/{taskId}");
				_logger.LogInformation($"✅ Successfully deleted from Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskDeleted error: {ex.Message}");
				_logger.LogError($"❌ StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 📊 Task Status Updated - ✅ FIXED
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

				// Lấy status mới từ history_items
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

				var dto = new { Status = newStatus };
				_logger.LogInformation($"📤 Sending status update to Dashboard API");
				await _apiClient.PatchAsync($"api/tasks-sync/{taskId}/status", dto);
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
				var taskDetails = await FetchTaskFromClickUp(taskId);

				if (taskDetails == null)
				{
					_logger.LogError($"❌ Cannot fetch task details: {taskId}");
					return;
				}

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var taskName = GetPropertySafe(task, "name");
				var status = GetNestedPropertySafe(task, "status", "status");
				var priority = GetNestedPropertySafe(task, "priority", "priority");
				var dueDate = GetPropertySafe(task, "due_date");
				var url = GetPropertySafe(task, "url");

				var assignees = new List<string>();
				if (task.TryGetProperty("assignees", out var assigneesArray))
				{
					foreach (var assignee in assigneesArray.EnumerateArray())
					{
						var username = GetPropertySafe(assignee, "username");
						if (!string.IsNullOrEmpty(username))
							assignees.Add(username);
					}
				}

				var dto = new
				{
					TaskId = taskId,
					Name = taskName,
					Status = status,
					Priority = priority ?? "normal",
					DueDate = dueDate,
					Url = url,
					Assignees = assignees
				};

				_logger.LogInformation($"📤 Syncing assignee changes");
				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
				_logger.LogInformation($"✅ Successfully synced assignee changes: {taskId}");
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
		// 🛠️ Helper Methods
		// =============================
		private string GetPropertySafe(JsonElement element, string propertyName)
		{
			return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? "" : "";
		}

		private string GetNestedPropertySafe(JsonElement element, string parent, string child)
		{
			if (element.TryGetProperty(parent, out var parentProp) &&
				parentProp.TryGetProperty(child, out var childProp))
			{
				return childProp.GetString() ?? "";
			}
			return "";
		}
	}
}