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
			_token = config["ClickUpSettings:Token"];
			_logger = logger;
			_apiClient = apiClient;

			_httpClient.BaseAddress = new Uri(config["ClickUpSettings:ApiBaseUrl"]);
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
		// 📥 Task Created
		// =============================
		private async Task HandleTaskCreated(JsonElement payload)
		{
			try
			{
				if (!payload.TryGetProperty("task", out var task))
				{
					_logger.LogWarning("⚠️ taskCreated: Missing 'task' property");
					return;
				}

				var taskId = GetPropertySafe(task, "id");
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

				_logger.LogInformation($"✅ Task created: {taskName} ({taskId}) | Status: {status}");

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

				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
				_logger.LogInformation($"✅ Synced to Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskCreated error: {ex.Message}\n{ex.StackTrace}");
			}
		}

		// =============================
		// 🔄 Task Updated
		// =============================
		private async Task HandleTaskUpdated(JsonElement payload)
		{
			try
			{
				if (!payload.TryGetProperty("task", out var task))
				{
					_logger.LogWarning("⚠️ taskUpdated: Missing 'task' property");
					return;
				}

				var taskId = GetPropertySafe(task, "id");
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

				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
				_logger.LogInformation($"✅ Updated task in Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskUpdated error: {ex.Message}\n{ex.StackTrace}");
			}
		}

		// =============================
		// 🗑️ Task Deleted
		// =============================
		private async Task HandleTaskDeleted(JsonElement payload)
		{
			try
			{
				var taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ taskDeleted: Missing 'task_id'");
					return;
				}

				_logger.LogInformation($"🗑️ Task deleted: {taskId}");

				await _apiClient.DeleteAsync($"api/tasks-sync/{taskId}");
				_logger.LogInformation($"✅ Deleted task from Dashboard: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskDeleted error: {ex.Message}\n{ex.StackTrace}");
			}
		}

		// =============================
		// 📊 Task Status Updated
		// =============================
		private async Task HandleTaskStatusUpdated(JsonElement payload)
		{
			try
			{
				if (!payload.TryGetProperty("task", out var task))
				{
					_logger.LogWarning("⚠️ taskStatusUpdated: Missing 'task' property");
					return;
				}

				var taskId = GetPropertySafe(task, "id");
				var newStatus = GetNestedPropertySafe(task, "status", "status");

				_logger.LogInformation($"📊 Task status updated: {taskId} → {newStatus}");

				var dto = new { Status = newStatus };
				await _apiClient.PatchAsync($"api/tasks-sync/{taskId}/status", dto);
				_logger.LogInformation($"✅ Updated status in Dashboard: {taskId} → {newStatus}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskStatusUpdated error: {ex.Message}\n{ex.StackTrace}");
			}
		}

		// =============================
		// 👤 Task Assignee Updated
		// =============================
		private async Task HandleTaskAssigneeUpdated(JsonElement payload)
		{
			try
			{
				if (!payload.TryGetProperty("task", out var task))
				{
					_logger.LogWarning("⚠️ taskAssigneeUpdated: Missing 'task' property");
					return;
				}

				var taskId = GetPropertySafe(task, "id");
				_logger.LogInformation($"👤 Task assignee updated: {taskId}");

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

				await _apiClient.PostAsync("api/tasks-sync/sync", dto);
				_logger.LogInformation($"✅ Synced assignee changes: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ HandleTaskAssigneeUpdated error: {ex.Message}\n{ex.StackTrace}");
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