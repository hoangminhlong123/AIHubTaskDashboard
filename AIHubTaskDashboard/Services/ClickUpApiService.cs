using System.Net.Http.Headers;
using System.Text.Json;

namespace AIHubTaskDashboard.Services
{
	public class ClickUpApiService
	{
		private readonly HttpClient _httpClient;
		private readonly string _token;
		private readonly string _listId;
		private readonly ILogger<ClickUpApiService> _logger;
		private readonly UserMappingService _userMapping;

		public ClickUpApiService(
			IConfiguration config,
			ILogger<ClickUpApiService> logger,
			UserMappingService userMapping)
		{
			_httpClient = new HttpClient();
			_token = config["ClickUpSettings:Token"] ?? "";
			_listId = config["ClickUpSettings:ListId"] ?? "";
			_logger = logger;
			_userMapping = userMapping;

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_token);
			_httpClient.DefaultRequestHeaders.Add("User-Agent", "AIHubTaskDashboard");
		}

		// ✅ CREATE Task với Assignee mapping
		public async Task<string?> CreateTaskAsync(string title, string description, string status, int? assigneeId = null)
		{
			try
			{
				_logger.LogInformation($"➕ Creating task in ClickUp: {title}");

				// 🔥 Map Dashboard assignee sang ClickUp user
				List<int>? clickUpAssignees = null;
				if (assigneeId.HasValue)
				{
					var clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);
					if (!string.IsNullOrEmpty(clickUpUserId))
					{
						// ClickUp yêu cầu assignees là array of integers
						if (int.TryParse(clickUpUserId, out var cuId))
						{
							clickUpAssignees = new List<int> { cuId };
							_logger.LogInformation($"✅ Mapped assignee: Dashboard {assigneeId} → ClickUp {clickUpUserId}");
						}
					}
					else
					{
						_logger.LogWarning($"⚠️ No ClickUp mapping found for Dashboard user {assigneeId}");
					}
				}

				var payload = new
				{
					name = title,
					description = description,
					status = MapDashboardStatusToClickUp(status),
					assignees = clickUpAssignees // 🔥 Thêm assignees
				};

				var response = await _httpClient.PostAsJsonAsync($"list/{_listId}/task", payload);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ ClickUp CreateTask failed: {response.StatusCode} - {content}");
					return null;
				}

				var result = JsonDocument.Parse(content).RootElement;
				var taskId = result.GetProperty("id").GetString();

				_logger.LogInformation($"✅ Task created in ClickUp: {taskId}");
				return taskId;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error creating task in ClickUp: {ex.Message}");
				return null;
			}
		}

		// ✅ UPDATE Task với Assignee mapping
		public async Task<bool> UpdateTaskAsync(string clickupId, string title, string description, string status, int? assigneeId = null)
		{
			try
			{
				_logger.LogInformation($"🔄 Updating task in ClickUp: {clickupId}");

				// 🔥 Map assignee nếu có
				List<object>? assignees = null;
				if (assigneeId.HasValue)
				{
					var clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);
					if (!string.IsNullOrEmpty(clickUpUserId))
					{
						if (int.TryParse(clickUpUserId, out var cuId))
						{
							assignees = new List<object>
							{
								new { add = cuId, rem = (int?)null }
							};
							_logger.LogInformation($"✅ Mapped assignee for update: Dashboard {assigneeId} → ClickUp {clickUpUserId}");
						}
					}
				}

				var payload = new
				{
					name = title,
					description = description,
					status = MapDashboardStatusToClickUp(status),
					assignees = assignees // 🔥 Update assignees
				};

				var response = await _httpClient.PutAsJsonAsync($"task/{clickupId}", payload);

				if (!response.IsSuccessStatusCode)
				{
					var content = await response.Content.ReadAsStringAsync();
					_logger.LogError($"❌ ClickUp UpdateTask failed: {response.StatusCode} - {content}");
					return false;
				}

				_logger.LogInformation($"✅ Task updated in ClickUp: {clickupId}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error updating task in ClickUp: {ex.Message}");
				return false;
			}
		}

		// ✅ DELETE Task
		public async Task<bool> DeleteTaskAsync(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🗑️ Deleting task in ClickUp: {clickupId}");

				var response = await _httpClient.DeleteAsync($"task/{clickupId}");

				if (!response.IsSuccessStatusCode)
				{
					var content = await response.Content.ReadAsStringAsync();
					_logger.LogError($"❌ ClickUp DeleteTask failed: {response.StatusCode} - {content}");
					return false;
				}

				_logger.LogInformation($"✅ Task deleted in ClickUp: {clickupId}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error deleting task in ClickUp: {ex.Message}");
				return false;
			}
		}

		private string MapDashboardStatusToClickUp(string dashboardStatus)
		{
			return dashboardStatus?.ToLower() switch
			{
				"to do" => "to do",
				"pending" => "to do",
				"in progress" => "in progress",
				"completed" => "complete",
				"done" => "complete",
				_ => "to do"
			};
		}
	}
}