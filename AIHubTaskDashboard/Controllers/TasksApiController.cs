using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	[ApiController]
	[Route("api/tasks-sync")]
	public class TasksApiController : ControllerBase
	{
		private readonly ILogger<TasksApiController> _logger;
		private readonly ApiClientService _apiClient;

		public TasksApiController(
			ILogger<TasksApiController> logger,
			ApiClientService apiClient)
		{
			_logger = logger;
			_apiClient = apiClient;
		}

		// =============================
		// 📥 SYNC Task (Create/Update)
		// =============================
		[HttpPost("sync")]
		public async Task<IActionResult> SyncTask([FromBody] ClickUpTaskDto dto)
		{
			try
			{
				_logger.LogInformation($"📥 Syncing task: {dto.TaskId} - {dto.Name}");

				// 🔍 CHECK: Task đã tồn tại chưa?
				var existingTaskJson = await TryGetExistingTask(dto.TaskId);

				var payload = new
				{
					clickup_id = dto.TaskId,
					title = dto.Name,
					description = BuildDescription(dto),
					status = MapClickUpStatus(dto.Status),
					progress_percentage = CalculateProgress(dto.Status),
					assignee_id = 1, // TODO: Map ClickUp assignee
					assigner_id = 1, // System user
					collaborators = new List<int>(),
					expected_output = "Auto-synced from ClickUp",
					deadline = ParseClickUpDate(dto.DueDate),
					notion_link = dto.Url
				};

				if (existingTaskJson != null)
				{
					// UPDATE existing task
					var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
					var taskId = existingTask.GetProperty("task_id").GetInt32();

					await _apiClient.PutAsync($"api/v1/tasks/{taskId}", payload);
					_logger.LogInformation($"✅ Task updated: {dto.TaskId}");
				}
				else
				{
					// CREATE new task
					await _apiClient.PostAsync("api/v1/tasks", payload);
					_logger.LogInformation($"✅ Task created: {dto.TaskId}");
				}

				return Ok(new { success = true, message = "Task synced", taskId = dto.TaskId });
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Sync error: {ex.Message}\n{ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		// =============================
		// 🗑️ DELETE Task
		// =============================
		[HttpDelete("{clickupTaskId}")]
		public async Task<IActionResult> DeleteTask(string clickupTaskId)
		{
			try
			{
				_logger.LogInformation($"🗑️ Deleting task: {clickupTaskId}");

				var taskJson = await TryGetExistingTask(clickupTaskId);

				if (taskJson == null)
				{
					_logger.LogWarning($"⚠️ Task not found: {clickupTaskId}");
					return NotFound(new { error = "Task not found", clickupTaskId });
				}

				var task = JsonDocument.Parse(taskJson).RootElement;
				var taskId = task.GetProperty("task_id").GetInt32();

				await _apiClient.DeleteAsync($"api/v1/tasks/{taskId}");

				_logger.LogInformation($"✅ Task deleted: {clickupTaskId}");
				return Ok(new { success = true, message = "Task deleted", clickupTaskId });
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Delete error: {ex.Message}\n{ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		// =============================
		// 📊 UPDATE Status
		// =============================
		[HttpPatch("{clickupTaskId}/status")]
		public async Task<IActionResult> UpdateStatus(string clickupTaskId, [FromBody] StatusUpdateDto dto)
		{
			try
			{
				_logger.LogInformation($"📊 Updating status: {clickupTaskId} → {dto.Status}");

				var taskJson = await TryGetExistingTask(clickupTaskId);

				if (taskJson == null)
				{
					_logger.LogWarning($"⚠️ Task not found: {clickupTaskId}");
					return NotFound(new { error = "Task not found", clickupTaskId });
				}

				var task = JsonDocument.Parse(taskJson).RootElement;
				var taskId = task.GetProperty("task_id").GetInt32();

				var mappedStatus = MapClickUpStatus(dto.Status);
				var progress = CalculateProgress(dto.Status);

				var payload = new
				{
					status = mappedStatus,
					progress_percentage = progress
				};

				await _apiClient.PutAsync($"api/v1/tasks/{taskId}", payload);

				_logger.LogInformation($"✅ Status updated: {clickupTaskId} → {mappedStatus}");
				return Ok(new { success = true, status = mappedStatus, progress });
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Status update error: {ex.Message}\n{ex.StackTrace}");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		// =============================
		// 🛠️ HELPER METHODS
		// =============================
		private async Task<string?> TryGetExistingTask(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🔍 Checking task: clickup_id={clickupId}");

				var response = await _apiClient.GetAsync($"api/v1/tasks?clickup_id={clickupId}");

				_logger.LogInformation($"📥 Backend response: {response}");

				if (string.IsNullOrEmpty(response))
				{
					_logger.LogInformation("⚠️ Empty response from backend");
					return null;
				}

				var tasks = JsonDocument.Parse(response).RootElement;

				if (tasks.ValueKind == JsonValueKind.Array && tasks.GetArrayLength() > 0)
				{
					_logger.LogInformation($"✅ Found existing task");
					return tasks[0].ToString();
				}

				if (tasks.ValueKind == JsonValueKind.Object)
				{
					_logger.LogInformation($"✅ Found existing task (single object)");
					return response;
				}

				_logger.LogInformation("⚠️ No task found with this clickup_id");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ TryGetExistingTask error: {ex.Message}");
				return null;
			}
		}

		private string BuildDescription(ClickUpTaskDto dto)
		{
			return $@"Synced from ClickUp
Status: {dto.Status}
Priority: {dto.Priority}
Assignees: {string.Join(", ", dto.Assignees)}";
		}

		private string MapClickUpStatus(string clickUpStatus)
		{
			return clickUpStatus?.ToLower() switch
			{
				"to do" => "Pending",
				"in progress" => "In Progress",
				"complete" => "Completed",
				"closed" => "Completed",
				"review" => "In Progress",
				_ => "Pending"
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
	}

	// DTO Classes
	public class ClickUpTaskDto
	{
		public string TaskId { get; set; }
		public string Name { get; set; }
		public string Status { get; set; }
		public string Priority { get; set; }
		public string? DueDate { get; set; }
		public string? Url { get; set; }
		public List<string> Assignees { get; set; } = new();
	}

	public class StatusUpdateDto
	{
		public string Status { get; set; }
	}
}