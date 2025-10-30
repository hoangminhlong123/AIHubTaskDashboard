using System.Net.Http.Headers;
using System.Text;
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

		public async Task<string?> CreateTaskAsync(string title, string description, string status, int? assigneeId = null)
		{
			var requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
			_logger.LogWarning($"🔷 [CLICKUP-{requestId}] ===== CREATE TASK IN CLICKUP =====");
			_logger.LogWarning($"🔷 [CLICKUP-{requestId}] Started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");

			try
			{
				_logger.LogInformation($"➕ [CLICKUP-{requestId}] Task: {title} | Assignee: {assigneeId}");

				// 🔥 BƯỚC 1: Lấy ClickUp User ID
				string? clickUpUserId = null;
				if (assigneeId.HasValue && assigneeId.Value > 0)
				{
					_logger.LogInformation($"🔍 [CLICKUP-{requestId}] Mapping Dashboard user {assigneeId}...");
					clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);
					_logger.LogInformation($"🔍 [CLICKUP-{requestId}] Mapping result: {clickUpUserId ?? "NULL"}");
				}

				// 🔥 BƯỚC 2: Build payload
				var payloadDict = new Dictionary<string, object>
				{
					["name"] = title,
					["description"] = description ?? "",
					["status"] = MapDashboardStatusToClickUp(status)
				};

				// ✅ Chỉ thêm assignees nếu có clickUpUserId hợp lệ
				if (!string.IsNullOrEmpty(clickUpUserId))
				{
					if (long.TryParse(clickUpUserId, out var userIdLong))
					{
						payloadDict["assignees"] = new[] { userIdLong };
						_logger.LogInformation($"✅ [CLICKUP-{requestId}] Using assignees as INT: [{userIdLong}]");
					}
					else
					{
						payloadDict["assignees"] = new[] { clickUpUserId };
						_logger.LogInformation($"✅ [CLICKUP-{requestId}] Using assignees as STRING: [\"{clickUpUserId}\"]");
					}
				}
				else
				{
					_logger.LogWarning($"⚠️ [CLICKUP-{requestId}] No assignee mapping, creating without assignee");
				}

				// 🔥 BƯỚC 3: Send request
				var jsonPayload = JsonSerializer.Serialize(payloadDict, new JsonSerializerOptions { WriteIndented = false });
				_logger.LogInformation($"📤 [CLICKUP-{requestId}] Sending HTTP POST to ClickUp...");

				var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
				var response = await _httpClient.PostAsync($"list/{_listId}/task", content);
				var responseContent = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📥 [CLICKUP-{requestId}] Response: {response.StatusCode}");

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [CLICKUP-{requestId}] CREATE FAILED: {response.StatusCode}");
					_logger.LogError($"❌ [CLICKUP-{requestId}] Error: {responseContent}");
					return null;
				}

				var result = JsonDocument.Parse(responseContent).RootElement;
				var taskId = result.GetProperty("id").GetString();

				_logger.LogWarning($"✅ [CLICKUP-{requestId}] ===== TASK CREATED: {taskId} =====");
				_logger.LogWarning($"✅ [CLICKUP-{requestId}] Completed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");

				return taskId;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [CLICKUP-{requestId}] EXCEPTION: {ex.Message}");
				_logger.LogError($"❌ [CLICKUP-{requestId}] StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// ✅ UPDATE Task - FIXED ASSIGNEE FORMAT
		public async Task<bool> UpdateTaskAsync(string clickupId, string title, string description, string status, int? assigneeId = null)
		{
			try
			{
				_logger.LogInformation($"🔄 Updating task in ClickUp: {clickupId}");

				var payloadDict = new Dictionary<string, object>
				{
					["name"] = title,
					["description"] = description ?? "",
					["status"] = MapDashboardStatusToClickUp(status)
				};

				// 🔥 Map assignee nếu có
				if (assigneeId.HasValue && assigneeId.Value > 0)
				{
					var clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);

					if (!string.IsNullOrEmpty(clickUpUserId))
					{
						// ClickUp update uses different format: {"assignees": {"add": [...], "rem": [...]}}
						if (long.TryParse(clickUpUserId, out var userIdLong))
						{
							payloadDict["assignees"] = new
							{
								add = new[] { userIdLong },
								rem = new long[] { }
							};
							_logger.LogInformation($"✅ Updating with assignee (int): {userIdLong}");
						}
						else
						{
							payloadDict["assignees"] = new
							{
								add = new[] { clickUpUserId },
								rem = new string[] { }
							};
							_logger.LogInformation($"✅ Updating with assignee (string): {clickUpUserId}");
						}
					}
				}

				var jsonPayload = JsonSerializer.Serialize(payloadDict, new JsonSerializerOptions { WriteIndented = true });
				_logger.LogInformation($"📤 ClickUp Update Request:\n{jsonPayload}");

				var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
				var response = await _httpClient.PutAsync($"task/{clickupId}", content);
				var responseContent = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📥 ClickUp Update Response: {response.StatusCode}");

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ ClickUp UpdateTask failed: {response.StatusCode}");
					_logger.LogError($"❌ Error details: {responseContent}");
					return false;
				}

				_logger.LogInformation($"✅ Task updated in ClickUp: {clickupId}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error updating task in ClickUp: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
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