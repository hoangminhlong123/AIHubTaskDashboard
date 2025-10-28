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
		private readonly string _teamId;
		private readonly ILogger<ClickUpService> _logger;

		public ClickUpService(IConfiguration config, ILogger<ClickUpService> logger)
		{
			_httpClient = new HttpClient();
			_token = config["ClickUpSettings:Token"];
			_teamId = config["ClickUpSettings:TeamId"];
			_logger = logger;

			_httpClient.BaseAddress = new Uri(config["ClickUpSettings:ApiBaseUrl"]);
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_token);
			_httpClient.DefaultRequestHeaders.Add("User-Agent", "AIHubTaskDashboard");
		}

		// =============================
		// 1️⃣ Sync thủ công - Lấy tất cả tasks
		// =============================
		public async Task<JsonElement> GetTasksAsync(string listId)
		{
			try
			{
				var response = await _httpClient.GetAsync($"list/{listId}/task?subtasks=true");
				var json = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ ClickUp API failed: {response.StatusCode} - {json}");
					throw new Exception($"ClickUp API failed: {response.StatusCode}");
				}

				_logger.LogInformation("✅ Successfully fetched tasks from ClickUp");
				return JsonDocument.Parse(json).RootElement;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error fetching tasks: {ex.Message}");
				throw;
			}
		}

		// =============================
		// 2️⃣ Xử lý Webhook Events
		// =============================
		public async Task HandleWebhookEventAsync(string eventType, JsonElement payload)
		{
			try
			{
				_logger.LogInformation($"📩 Received webhook: {eventType}");

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
				_logger.LogError($"❌ Error handling event {eventType}: {ex.Message}");
				throw;
			}
		}

		// =============================
		// 3️⃣ Task Created
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

				_logger.LogInformation($"✅ Task created: {taskName} ({taskId}) | Status: {status}");

				// TODO: Save to database
				// await SaveTaskToDatabase(taskId, taskName, status, priority, dueDate);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error in HandleTaskCreated: {ex.Message}");
			}

			await Task.CompletedTask;
		}

		// =============================
		// 4️⃣ Task Updated
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

				_logger.LogInformation($"🔄 Task updated: {taskName} ({taskId}) | Status: {status}");

				// TODO: Update database
				// await UpdateTaskInDatabase(taskId, taskName, status);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error in HandleTaskUpdated: {ex.Message}");
			}

			await Task.CompletedTask;
		}

		// =============================
		// 5️⃣ Task Deleted
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

				// TODO: Delete from database
				// await DeleteTaskFromDatabase(taskId);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error in HandleTaskDeleted: {ex.Message}");
			}

			await Task.CompletedTask;
		}

		// =============================
		// 6️⃣ Task Status Updated
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

				// TODO: Update status in database
				// await UpdateTaskStatusInDatabase(taskId, newStatus);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error in HandleTaskStatusUpdated: {ex.Message}");
			}

			await Task.CompletedTask;
		}

		// =============================
		// 7️⃣ Task Assignee Updated
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

				_logger.LogInformation($"👤 Task assignee updated: {taskId} → [{string.Join(", ", assignees)}]");

				// TODO: Update assignees in database
				// await UpdateTaskAssigneesInDatabase(taskId, assignees);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error in HandleTaskAssigneeUpdated: {ex.Message}");
			}

			await Task.CompletedTask;
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