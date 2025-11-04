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
		private readonly string _spaceId;
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
			_spaceId = config["ClickUpSettings:SpaceId"] ?? "";
			_logger = logger;
			_userMapping = userMapping;

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);

			// 🔥 FIX: ClickUp yêu cầu token trực tiếp, KHÔNG có "Bearer"
			_httpClient.DefaultRequestHeaders.Clear();
			_httpClient.DefaultRequestHeaders.Add("Authorization", _token);

			_logger.LogInformation($"🔧 [INIT] ClickUp API initialized");
			_logger.LogInformation($"🔧 [INIT] Base URL: {baseUrl}");
			_logger.LogInformation($"🔧 [INIT] Token: {_token.Substring(0, Math.Min(15, _token.Length))}...");
		}

		// 🔥 Get Space Tags (DEPRECATED - có thể gây lỗi Unauthorized)
		public async Task<JsonElement> GetSpaceTagsAsync()
		{
			try
			{
				_logger.LogInformation($"🏷️ Fetching tags from Space: {_spaceId}");

				var response = await _httpClient.GetAsync($"space/{_spaceId}/tag");
				var responseContent = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogWarning($"⚠️ GetSpaceTags failed: {response.StatusCode}");
					_logger.LogDebug($"⚠️ Response: {responseContent}");
					return JsonDocument.Parse("[]").RootElement;
				}

				var result = JsonDocument.Parse(responseContent).RootElement;

				if (result.TryGetProperty("tags", out var tagsArray))
				{
					_logger.LogInformation($"✅ Retrieved {tagsArray.GetArrayLength()} tags from ClickUp");
					return tagsArray;
				}

				return JsonDocument.Parse("[]").RootElement;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error fetching space tags: {ex.Message}");
				return JsonDocument.Parse("[]").RootElement;
			}
		}

		// 🔥 Get Task Tags (PUBLIC - for syncing)
		public async Task<List<string>> GetTaskTagsAsync(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🔍 [TAGS] Fetching tags for task: {clickupId}");

				var response = await _httpClient.GetAsync($"task/{clickupId}");
				var responseContent = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogWarning($"⚠️ Cannot fetch task tags for {clickupId}: {response.StatusCode}");
					_logger.LogDebug($"⚠️ Response: {responseContent}");
					return new List<string>();
				}

				_logger.LogDebug($"📥 [TAGS] Response for {clickupId}: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...");

				var result = JsonDocument.Parse(responseContent).RootElement;

				if (result.TryGetProperty("tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
				{
					var tags = new List<string>();
					foreach (var tag in tagsArray.EnumerateArray())
					{
						if (tag.TryGetProperty("name", out var tagName))
						{
							var name = tagName.GetString();
							if (!string.IsNullOrEmpty(name))
							{
								tags.Add(name);
							}
						}
					}

					_logger.LogInformation($"✅ [TAGS] Task {clickupId} has {tags.Count} tags: {string.Join(", ", tags)}");
					return tags;
				}

				_logger.LogInformation($"ℹ️ [TAGS] Task {clickupId} has no tags");
				return new List<string>();
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error getting task tags for {clickupId}: {ex.Message}");
				return new List<string>();
			}
		}

		// ✅ CREATE Task - WITH TAGS
		public async Task<string?> CreateTaskAsync(string title, string description, string status, int? assigneeId = null, List<string>? tags = null)
		{
			try
			{
				_logger.LogInformation($"➕ Creating task in ClickUp: {title} | Dashboard assignee_id: {assigneeId}");

				// Map assignee
				string? clickUpUserId = null;
				if (assigneeId.HasValue && assigneeId.Value > 0)
				{
					clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);
					_logger.LogInformation($"🔍 Mapping result: Dashboard {assigneeId} → ClickUp {clickUpUserId ?? "NULL"}");
				}

				// Build payload
				var payloadDict = new Dictionary<string, object>
				{
					["name"] = title,
					["description"] = description ?? "",
					["status"] = MapDashboardStatusToClickUp(status)
				};

				// Add assignees if available
				if (!string.IsNullOrEmpty(clickUpUserId))
				{
					if (long.TryParse(clickUpUserId, out var userIdLong))
					{
						payloadDict["assignees"] = new[] { userIdLong };
						_logger.LogInformation($"✅ Using assignees as INT: [{userIdLong}]");
					}
					else
					{
						payloadDict["assignees"] = new[] { clickUpUserId };
						_logger.LogInformation($"✅ Using assignees as STRING: [\"{clickUpUserId}\"]");
					}
				}
				else
				{
					_logger.LogWarning($"⚠️ No ClickUp user mapping found, creating task without assignee");
				}

				// 🔥 Add tags if provided
				if (tags != null && tags.Count > 0)
				{
					payloadDict["tags"] = tags;
					_logger.LogInformation($"🏷️ Adding {tags.Count} tags: {string.Join(", ", tags)}");
				}

				// Serialize and send
				var jsonPayload = JsonSerializer.Serialize(payloadDict, new JsonSerializerOptions
				{
					WriteIndented = true
				});
				_logger.LogInformation($"📤 ClickUp API Request:\n{jsonPayload}");

				var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
				var response = await _httpClient.PostAsync($"list/{_listId}/task", content);
				var responseContent = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📥 ClickUp API Response: {response.StatusCode}");
				_logger.LogInformation($"📥 Response body: {responseContent}");

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ ClickUp CreateTask failed: {response.StatusCode}");
					_logger.LogError($"❌ Error details: {responseContent}");
					return null;
				}

				var result = JsonDocument.Parse(responseContent).RootElement;
				var taskId = result.GetProperty("id").GetString();

				_logger.LogInformation($"✅ Task created in ClickUp: {taskId}");

				// Verify tags
				if (tags != null && tags.Count > 0 && result.TryGetProperty("tags", out var responseTags))
				{
					_logger.LogInformation($"✅ ClickUp returned tags: {responseTags}");
				}

				return taskId;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error creating task in ClickUp: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// ✅ UPDATE Task - WITH TAGS
		public async Task<bool> UpdateTaskAsync(string clickupId, string title, string description, string status, int? assigneeId = null, List<string>? tags = null)
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

				// Map assignee if provided
				if (assigneeId.HasValue && assigneeId.Value > 0)
				{
					var clickUpUserId = await _userMapping.MapDashboardUserToClickUp(assigneeId.Value);

					if (!string.IsNullOrEmpty(clickUpUserId))
					{
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

				// 🔥 Update tags if provided
				if (tags != null)
				{
					// Get current tags to determine what to add/remove
					var currentTags = await GetTaskTagsAsync(clickupId);

					var tagsToAdd = tags.Except(currentTags).ToList();
					var tagsToRemove = currentTags.Except(tags).ToList();

					if (tagsToAdd.Count > 0 || tagsToRemove.Count > 0)
					{
						_logger.LogInformation($"🏷️ Updating tags: +{tagsToAdd.Count} -{tagsToRemove.Count}");

						// Add new tags
						foreach (var tag in tagsToAdd)
						{
							await AddTagToTaskAsync(clickupId, tag);
						}

						// Remove old tags
						foreach (var tag in tagsToRemove)
						{
							await RemoveTagFromTaskAsync(clickupId, tag);
						}
					}
					else
					{
						_logger.LogInformation($"ℹ️ [TAGS] No tag changes needed");
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

		// 🔥 Add Tag to Task
		private async Task<bool> AddTagToTaskAsync(string clickupId, string tagName)
		{
			try
			{
				_logger.LogInformation($"🏷️ Adding tag '{tagName}' to task {clickupId}");

				// 🔥 ClickUp API: POST /task/{task_id}/tag/{tag_name}
				// Body không cần thiết cho endpoint này
				var response = await _httpClient.PostAsync($"task/{clickupId}/tag/{Uri.EscapeDataString(tagName)}", null);

				if (response.IsSuccessStatusCode)
				{
					_logger.LogInformation($"✅ Tag '{tagName}' added successfully");
					return true;
				}
				else
				{
					var errorContent = await response.Content.ReadAsStringAsync();
					_logger.LogWarning($"⚠️ Failed to add tag: {response.StatusCode} - {errorContent}");
					return false;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error adding tag: {ex.Message}");
				return false;
			}
		}

		// 🔥 Remove Tag from Task
		private async Task<bool> RemoveTagFromTaskAsync(string clickupId, string tagName)
		{
			try
			{
				_logger.LogInformation($"🏷️ Removing tag '{tagName}' from task {clickupId}");

				var response = await _httpClient.DeleteAsync($"task/{clickupId}/tag/{Uri.EscapeDataString(tagName)}");

				if (response.IsSuccessStatusCode)
				{
					_logger.LogInformation($"✅ Tag '{tagName}' removed successfully");
					return true;
				}
				else
				{
					var errorContent = await response.Content.ReadAsStringAsync();
					_logger.LogWarning($"⚠️ Failed to remove tag: {response.StatusCode} - {errorContent}");
					return false;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Error removing tag: {ex.Message}");
				return false;
			}
		}

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
                "to do" => "To Do",
                "pending" => "To Do",
                "in progress" => "In Progress",
                "completed" => "Completed",
                "done" => "Completed",
                _ => "To Do"
            };
        }

    }
}