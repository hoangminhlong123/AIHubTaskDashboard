using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AIHubTaskDashboard.Services
{
	/// <summary>
	/// ⚡ ULTRA FAST MODE with DETAILED LOGGING + FIXED AUTHENTICATION
	/// </summary>
	public class ClickUpService
	{
		private readonly HttpClient _httpClient;
		private readonly string _token;
		private readonly ILogger<ClickUpService> _logger;
		private readonly ApiClientService _apiClient;
		private readonly UserMappingService _userMapping;
		private readonly IConfiguration _config;

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
			_config = config;

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);

			// 🔥 CRITICAL FIX: Match ClickUpApiService's working header setup
			_httpClient.DefaultRequestHeaders.Clear();
			_httpClient.DefaultRequestHeaders.Add("Authorization", _token);
			_httpClient.DefaultRequestHeaders.Accept.Clear();
			_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

			_httpClient.Timeout = TimeSpan.FromSeconds(30);

			_logger.LogInformation("🔧 [INIT] ClickUpService initialized");
			_logger.LogInformation($"🔧 [INIT] Base URL: {baseUrl}");
			_logger.LogInformation($"🔧 [INIT] Token: {_token.Substring(0, Math.Min(15, _token.Length))}...");
			_logger.LogInformation($"🔧 [INIT] Token length: {_token.Length}");
			_logger.LogInformation($"🔧 [INIT] Authorization header set: {_httpClient.DefaultRequestHeaders.Contains("Authorization")}");

			// 🔥 CRITICAL: Verify token format
			if (!_token.StartsWith("pk_"))
			{
				_logger.LogError("❌ [INIT] Invalid token format! ClickUp tokens should start with 'pk_'");
			}
		}

		public async Task HandleWebhookEventAsync(string eventType, JsonElement payload)
		{
			try
			{
				_logger.LogInformation("═══════════════════════════════════════════════════════");
				_logger.LogInformation($"⚡ [WEBHOOK] Processing Event: {eventType}");
				_logger.LogInformation($"⏰ [WEBHOOK] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
				_logger.LogInformation("═══════════════════════════════════════════════════════");

				switch (eventType)
				{
					case "taskCreated":
						_logger.LogInformation("🆕 [WEBHOOK] Event Type: TASK CREATED");
						await HandleTaskCreatedUltraFast(payload);
						break;
					case "taskUpdated":
						_logger.LogInformation("🔄 [WEBHOOK] Event Type: TASK UPDATED");
						await HandleTaskUpdated(payload);
						break;
					case "taskDeleted":
						_logger.LogInformation("🗑️ [WEBHOOK] Event Type: TASK DELETED");
						await HandleTaskDeleted(payload);
						break;
					case "taskStatusUpdated":
						_logger.LogInformation("📊 [WEBHOOK] Event Type: TASK STATUS UPDATED");
						await HandleTaskStatusUpdated(payload);
						break;
					case "taskAssigneeUpdated":
						_logger.LogInformation("👤 [WEBHOOK] Event Type: TASK ASSIGNEE UPDATED");
						await HandleTaskAssigneeUpdated(payload);
						break;
					default:
						_logger.LogDebug($"⏭️ [WEBHOOK] Skipping unhandled event: {eventType}");
						break;
				}

				_logger.LogInformation("═══════════════════════════════════════════════════════");
				_logger.LogInformation($"✅ [WEBHOOK] Event Processing Completed: {eventType}");
				_logger.LogInformation("═══════════════════════════════════════════════════════");
			}
			catch (Exception ex)
			{
				_logger.LogError("═══════════════════════════════════════════════════════");
				_logger.LogError($"❌ [WEBHOOK] Fatal Error: {ex.Message}");
				_logger.LogError($"❌ [WEBHOOK] StackTrace: {ex.StackTrace}");
				_logger.LogError("═══════════════════════════════════════════════════════");
			}
		}

		/// <summary>
		/// ⚡ ULTRA FAST: NO CHECKS - Fetch + Create (with detailed logging)
		/// </summary>
		private async Task HandleTaskCreatedUltraFast(JsonElement payload)
		{
			string? taskId = null;
			try
			{
				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  STEP 1: EXTRACT TASK ID FROM PAYLOAD              │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");

				taskId = GetPropertySafe(payload, "task_id");

				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ [CREATE] No task_id found in payload");
					_logger.LogWarning($"⚠️ [CREATE] Payload keys: {string.Join(", ", GetPayloadKeys(payload))}");
					return;
				}

				_logger.LogInformation($"✅ [CREATE] Extracted Task ID: {taskId}");

				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  STEP 2: FETCH TASK DETAILS FROM CLICKUP           │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");
				_logger.LogInformation($"🌐 [CREATE] Calling ClickUp API: GET task/{taskId}");

				var taskDetails = await FetchTaskFromClickUp(taskId);
				if (taskDetails == null)
				{
					_logger.LogError($"❌ [CREATE] Failed to fetch task from ClickUp: {taskId}");
					_logger.LogError($"❌ [CREATE] Check if:");
					_logger.LogError($"   1. Token is valid and has correct permissions");
					_logger.LogError($"   2. Task ID '{taskId}' exists in ClickUp");
					_logger.LogError($"   3. Network connectivity to ClickUp API");
					return;
				}

				_logger.LogInformation($"✅ [CREATE] Successfully fetched task details ({taskDetails.Length} chars)");

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var taskName = GetPropertySafe(task, "name");
				var status = GetNestedPropertySafe(task, "status", "status");
				var dueDate = GetPropertySafe(task, "due_date");
				var url = GetPropertySafe(task, "url");
				var description = GetPropertySafe(task, "description");

				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  STEP 3: PARSE TASK DATA                           │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");
				_logger.LogInformation($"📋 [CREATE] Task Name: {taskName}");
				_logger.LogInformation($"📊 [CREATE] Status: {status}");
				_logger.LogInformation($"📅 [CREATE] Due Date: {dueDate}");
				_logger.LogInformation($"🔗 [CREATE] URL: {url}");
				_logger.LogInformation($"📝 [CREATE] Description Length: {description?.Length ?? 0} chars");

				var mappedStatus = MapClickUpStatus(status);
				var progress = CalculateProgress(status);
				var deadline = ParseClickUpDate(dueDate);

				_logger.LogInformation($"🔄 [CREATE] Mapped Status: {status} → {mappedStatus}");
				_logger.LogInformation($"📈 [CREATE] Progress: {progress}%");
				_logger.LogInformation($"⏰ [CREATE] Deadline: {deadline}");

				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  STEP 4: CREATE TASK IN DASHBOARD                  │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");

				var taskDto = new
				{
					clickup_id = taskId,
					title = taskName ?? "Untitled Task",
					description = string.IsNullOrEmpty(description)
						? $"Auto-synced from ClickUp"
						: description,
					status = mappedStatus,
					progress_percentage = progress,
					assignee_id = 1,
					assigner_id = 1,
					collaborators = new List<int> { 1 },
					expected_output = "Auto-synced from ClickUp",
					deadline = deadline,
					notion_link = url ?? ""
				};

				_logger.LogInformation($"📤 [CREATE] Sending POST request to: api/v1/tasks");
				_logger.LogInformation($"📦 [CREATE] Payload: {JsonSerializer.Serialize(taskDto, new JsonSerializerOptions { WriteIndented = true })}");

				try
				{
					var response = await _apiClient.PostAsync("api/v1/tasks", taskDto);

					if (!string.IsNullOrEmpty(response))
					{
						_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
						_logger.LogInformation("│  ✅ SUCCESS - TASK CREATED IN DASHBOARD            │");
						_logger.LogInformation("└─────────────────────────────────────────────────────┘");
						_logger.LogInformation($"✅ [CREATE] Task created successfully!");
						_logger.LogInformation($"📋 [CREATE] Task Name: {taskName}");
						_logger.LogInformation($"🆔 [CREATE] ClickUp ID: {taskId}");
						_logger.LogInformation($"📊 [CREATE] Status: {mappedStatus}");
						_logger.LogInformation($"📈 [CREATE] Progress: {progress}%");
						_logger.LogInformation($"📝 [CREATE] Response: {response}");
					}
					else
					{
						_logger.LogWarning("⚠️ [CREATE] Empty response from Dashboard API");
						_logger.LogWarning($"⚠️ [CREATE] Task ID: {taskId}");
					}
				}
				catch (Exception ex)
				{
					_logger.LogError("┌─────────────────────────────────────────────────────┐");
					_logger.LogError("│  ❌ FAILED - DASHBOARD API ERROR                    │");
					_logger.LogError("└─────────────────────────────────────────────────────┘");
					_logger.LogError($"❌ [CREATE] Dashboard API Error: {ex.Message}");
					_logger.LogError($"❌ [CREATE] Task ID: {taskId}");
					_logger.LogError($"❌ [CREATE] Exception Type: {ex.GetType().Name}");
					_logger.LogError($"❌ [CREATE] StackTrace: {ex.StackTrace}");

					_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
					_logger.LogInformation("│  💾 FALLBACK: SAVING TO LOCAL FILE                 │");
					_logger.LogInformation("└─────────────────────────────────────────────────────┘");
					await SaveTaskToLocalFile(taskId, task);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("┌─────────────────────────────────────────────────────┐");
				_logger.LogError("│  ❌ CRITICAL ERROR IN TASK CREATION                 │");
				_logger.LogError("└─────────────────────────────────────────────────────┘");
				_logger.LogError($"❌ [CREATE] Critical Error: {ex.Message}");
				_logger.LogError($"❌ [CREATE] Task ID: {taskId ?? "UNKNOWN"}");
				_logger.LogError($"❌ [CREATE] Exception Type: {ex.GetType().Name}");
				_logger.LogError($"❌ [CREATE] StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 💾 FALLBACK: Save to Local File
		// =============================
		private async Task SaveTaskToLocalFile(string clickupId, JsonElement task)
		{
			try
			{
				var fileName = $"clickup_tasks_{DateTime.Now:yyyyMMdd}.json";
				var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
				var filePath = Path.Combine(dataDir, fileName);

				_logger.LogInformation($"💾 [FALLBACK] Creating directory: {dataDir}");
				Directory.CreateDirectory(dataDir);

				List<JsonElement> tasks = new();
				if (File.Exists(filePath))
				{
					_logger.LogInformation($"💾 [FALLBACK] File exists, loading: {filePath}");
					var json = await File.ReadAllTextAsync(filePath);
					var doc = JsonDocument.Parse(json);
					tasks = doc.RootElement.EnumerateArray().ToList();
					_logger.LogInformation($"💾 [FALLBACK] Loaded {tasks.Count} existing tasks");
				}

				tasks.Add(task);
				_logger.LogInformation($"💾 [FALLBACK] Added new task, total: {tasks.Count}");

				var output = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
				await File.WriteAllTextAsync(filePath, output);

				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  ✅ FALLBACK SUCCESSFUL                             │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");
				_logger.LogInformation($"💾 [FALLBACK] Saved to: {filePath}");
				_logger.LogInformation($"💾 [FALLBACK] Task ID: {clickupId}");
				_logger.LogInformation($"💾 [FALLBACK] File size: {new FileInfo(filePath).Length} bytes");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [FALLBACK] Failed to save: {ex.Message}");
				_logger.LogError($"❌ [FALLBACK] StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 🔄 Task Updated
		// =============================
		private async Task HandleTaskUpdated(JsonElement payload)
		{
			string? taskId = null;
			try
			{
				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  UPDATE TASK WORKFLOW                               │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");

				taskId = GetPropertySafe(payload, "task_id");
				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ [UPDATE] No task_id in payload");
					return;
				}

				_logger.LogInformation($"🔍 [UPDATE] Task ID: {taskId}");
				_logger.LogInformation($"🔍 [UPDATE] Checking if task exists in Dashboard...");

				var existingTaskJson = await TryGetExistingTask(taskId);
				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ [UPDATE] Task not found in Dashboard: {taskId}");
					_logger.LogWarning($"⚠️ [UPDATE] Creating new task instead...");
					await HandleTaskCreatedUltraFast(payload);
					return;
				}

				_logger.LogInformation($"✅ [UPDATE] Found existing task in Dashboard");

				_logger.LogInformation($"🌐 [UPDATE] Fetching latest data from ClickUp...");
				var taskDetails = await FetchTaskFromClickUp(taskId);
				if (taskDetails == null)
				{
					_logger.LogError($"❌ [UPDATE] Failed to fetch from ClickUp: {taskId}");
					return;
				}

				var task = JsonDocument.Parse(taskDetails).RootElement;
				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				_logger.LogInformation($"📋 [UPDATE] Dashboard Task ID: {dbTaskId}");
				_logger.LogInformation($"📋 [UPDATE] ClickUp Task ID: {taskId}");

				var updatePayload = new
				{
					title = GetPropertySafe(task, "name"),
					description = GetPropertySafe(task, "description"),
					status = MapClickUpStatus(GetNestedPropertySafe(task, "status", "status")),
					progress_percentage = CalculateProgress(GetNestedPropertySafe(task, "status", "status")),
					deadline = ParseClickUpDate(GetPropertySafe(task, "due_date")),
					notion_link = GetPropertySafe(task, "url")
				};

				_logger.LogInformation($"📤 [UPDATE] Sending update to Dashboard...");
				_logger.LogInformation($"📦 [UPDATE] Payload: {JsonSerializer.Serialize(updatePayload)}");

				await _apiClient.PutAsync($"api/v1/tasks/{dbTaskId}", updatePayload);

				_logger.LogInformation("✅ [UPDATE] Task updated successfully!");
				_logger.LogInformation($"✅ [UPDATE] Task ID: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [UPDATE] Error: {ex.Message}");
				_logger.LogError($"❌ [UPDATE] Task ID: {taskId ?? "UNKNOWN"}");
				_logger.LogError($"❌ [UPDATE] StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 🗑️ Task Deleted
		// =============================
		private async Task HandleTaskDeleted(JsonElement payload)
		{
			string? taskId = null;
			try
			{
				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  DELETE TASK WORKFLOW                               │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");

				taskId = GetPropertySafe(payload, "task_id");
				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ [DELETE] No task_id in payload");
					return;
				}

				_logger.LogInformation($"🗑️ [DELETE] Task ID: {taskId}");
				_logger.LogInformation($"🔍 [DELETE] Looking up task in Dashboard...");

				var existingTaskJson = await TryGetExistingTask(taskId);
				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ [DELETE] Task not found in Dashboard: {taskId}");
					return;
				}

				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				_logger.LogInformation($"✅ [DELETE] Found task, Dashboard ID: {dbTaskId}");
				_logger.LogInformation($"📤 [DELETE] Sending delete request...");

				await _apiClient.DeleteAsync($"api/v1/tasks/{dbTaskId}");

				_logger.LogInformation("✅ [DELETE] Task deleted successfully!");
				_logger.LogInformation($"✅ [DELETE] Task ID: {taskId}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DELETE] Error: {ex.Message}");
				_logger.LogError($"❌ [DELETE] Task ID: {taskId ?? "UNKNOWN"}");
				_logger.LogError($"❌ [DELETE] StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 📊 Task Status Updated
		// =============================
		private async Task HandleTaskStatusUpdated(JsonElement payload)
		{
			string? taskId = null;
			try
			{
				_logger.LogInformation("┌─────────────────────────────────────────────────────┐");
				_logger.LogInformation("│  STATUS UPDATE WORKFLOW                             │");
				_logger.LogInformation("└─────────────────────────────────────────────────────┘");

				taskId = GetPropertySafe(payload, "task_id");
				if (string.IsNullOrEmpty(taskId))
				{
					_logger.LogWarning("⚠️ [STATUS] No task_id in payload");
					return;
				}

				_logger.LogInformation($"📊 [STATUS] Task ID: {taskId}");
				_logger.LogInformation($"🔍 [STATUS] Looking up task...");

				var existingTaskJson = await TryGetExistingTask(taskId);
				if (existingTaskJson == null)
				{
					_logger.LogWarning($"⚠️ [STATUS] Task not found: {taskId}");
					return;
				}

				var newStatus = "";
				if (payload.TryGetProperty("history_items", out var historyItems) && historyItems.GetArrayLength() > 0)
				{
					var lastHistory = historyItems[historyItems.GetArrayLength() - 1];
					if (lastHistory.TryGetProperty("after", out var after))
					{
						newStatus = GetPropertySafe(after, "status");
						_logger.LogInformation($"📋 [STATUS] Status from history: {newStatus}");
					}
				}

				if (string.IsNullOrEmpty(newStatus))
				{
					_logger.LogInformation($"🌐 [STATUS] No status in history, fetching from ClickUp...");
					var taskDetails = await FetchTaskFromClickUp(taskId);
					if (taskDetails != null)
					{
						var task = JsonDocument.Parse(taskDetails).RootElement;
						newStatus = GetNestedPropertySafe(task, "status", "status");
						_logger.LogInformation($"📋 [STATUS] Status from API: {newStatus}");
					}
				}

				var existingTask = JsonDocument.Parse(existingTaskJson).RootElement;
				var dbTaskId = existingTask.GetProperty("task_id").GetInt32();

				var mappedStatus = MapClickUpStatus(newStatus);
				var progress = CalculateProgress(newStatus);

				_logger.LogInformation($"🔄 [STATUS] Original: {newStatus}");
				_logger.LogInformation($"🔄 [STATUS] Mapped: {mappedStatus}");
				_logger.LogInformation($"📈 [STATUS] Progress: {progress}%");

				var updatePayload = new
				{
					status = mappedStatus,
					progress_percentage = progress
				};

				_logger.LogInformation($"📤 [STATUS] Updating task {dbTaskId}...");
				await _apiClient.PutAsync($"api/v1/tasks/{dbTaskId}", updatePayload);

				_logger.LogInformation($"✅ [STATUS] Updated: {taskId} → {newStatus}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [STATUS] Error: {ex.Message}");
				_logger.LogError($"❌ [STATUS] Task ID: {taskId ?? "UNKNOWN"}");
				_logger.LogError($"❌ [STATUS] StackTrace: {ex.StackTrace}");
			}
		}

		// =============================
		// 👤 Task Assignee Updated
		// =============================
		private async Task HandleTaskAssigneeUpdated(JsonElement payload)
		{
			_logger.LogInformation("👤 [ASSIGNEE] Delegating to HandleTaskUpdated");
			await HandleTaskUpdated(payload);
		}

		// =============================
		// 🌐 Fetch Task từ ClickUp API (FIXED)
		// =============================
		private async Task<string?> FetchTaskFromClickUp(string taskId)
		{
			try
			{
				var url = $"task/{taskId}";
				_logger.LogInformation($"🌐 [FETCH] Calling ClickUp API: {url}");
				_logger.LogInformation($"🌐 [FETCH] Full URL: {_httpClient.BaseAddress}{url}");

				// 🔍 DEBUG: Log all headers being sent
				_logger.LogInformation($"🔍 [FETCH] Request Headers:");
				foreach (var header in _httpClient.DefaultRequestHeaders)
				{
					var value = string.Join(", ", header.Value);
					// Mask token for security
					if (header.Key == "Authorization" && value.Length > 20)
					{
						value = value.Substring(0, 15) + "...";
					}
					_logger.LogInformation($"   - {header.Key}: {value}");
				}

				// 🔥 CRITICAL: Don't create new request, use HttpClient directly
				// This matches the working ClickUpApiService pattern
				var response = await _httpClient.GetAsync(url);
				var content = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📡 [FETCH] Response Status: {response.StatusCode} ({(int)response.StatusCode})");
				_logger.LogInformation($"📡 [FETCH] Response Length: {content?.Length ?? 0} chars");

				// 🔍 DEBUG: Log response headers
				_logger.LogInformation($"📡 [FETCH] Response Headers:");
				foreach (var header in response.Headers)
				{
					_logger.LogInformation($"   - {header.Key}: {string.Join(", ", header.Value)}");
				}

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [FETCH] ClickUp API Error: {response.StatusCode}");
					_logger.LogError($"❌ [FETCH] Request URL: {response.RequestMessage?.RequestUri}");
					_logger.LogError($"❌ [FETCH] Response Body: {content}");

					// 🔥 Try to parse error details
					try
					{
						var errorJson = JsonDocument.Parse(content);
						if (errorJson.RootElement.TryGetProperty("err", out var errProp))
						{
							_logger.LogError($"❌ [FETCH] Error Message: {errProp.GetString()}");
						}
						if (errorJson.RootElement.TryGetProperty("ECODE", out var ecodeProp))
						{
							_logger.LogError($"❌ [FETCH] Error Code: {ecodeProp.GetString()}");
						}
					}
					catch { }

					return null;
				}

				_logger.LogInformation($"✅ [FETCH] Successfully fetched task: {taskId}");
				_logger.LogDebug($"📥 [FETCH] Response preview: {content.Substring(0, Math.Min(200, content.Length))}...");

				return content;
			}
			catch (HttpRequestException httpEx)
			{
				_logger.LogError($"❌ [FETCH] HTTP Request Exception: {httpEx.Message}");
				_logger.LogError($"❌ [FETCH] Inner Exception: {httpEx.InnerException?.Message}");
				return null;
			}
			catch (TaskCanceledException timeoutEx)
			{
				_logger.LogError($"❌ [FETCH] Request Timeout: {timeoutEx.Message}");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [FETCH] Exception: {ex.GetType().Name}");
				_logger.LogError($"❌ [FETCH] Message: {ex.Message}");
				_logger.LogError($"❌ [FETCH] StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// =============================
		// 🔍 Get Existing Task by ClickUp ID
		// =============================
		private async Task<string?> TryGetExistingTask(string clickupId)
		{
			try
			{
				_logger.LogInformation($"🔍 [LOOKUP] Searching for clickup_id: {clickupId}");
				_logger.LogInformation($"🔍 [LOOKUP] Query: api/v1/tasks?clickup_id={clickupId}");

				var response = await _apiClient.GetAsync($"api/v1/tasks?clickup_id={clickupId}");

				if (!string.IsNullOrEmpty(response))
				{
					var result = JsonDocument.Parse(response).RootElement;

					if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
					{
						_logger.LogInformation($"✅ [LOOKUP] Found task in array (count: {result.GetArrayLength()})");
						return result[0].ToString();
					}

					if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("task_id", out _))
					{
						_logger.LogInformation($"✅ [LOOKUP] Found task as object");
						return response;
					}
				}

				_logger.LogInformation($"🔍 [LOOKUP] Not found by query, searching all tasks...");
				var allTasksResponse = await _apiClient.GetAsync("api/v1/tasks");
				if (!string.IsNullOrEmpty(allTasksResponse))
				{
					var allTasks = JsonDocument.Parse(allTasksResponse).RootElement;
					if (allTasks.ValueKind == JsonValueKind.Array)
					{
						_logger.LogInformation($"🔍 [LOOKUP] Searching through {allTasks.GetArrayLength()} tasks...");
						foreach (var task in allTasks.EnumerateArray())
						{
							if (task.TryGetProperty("clickup_id", out var existingClickupId))
							{
								if (existingClickupId.GetString() == clickupId)
								{
									_logger.LogInformation($"✅ [LOOKUP] Found match in all tasks");
									return task.ToString();
								}
							}
						}
					}
				}

				_logger.LogWarning($"⚠️ [LOOKUP] Task not found: {clickupId}");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [LOOKUP] Error: {ex.Message}");
				_logger.LogError($"❌ [LOOKUP] StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// =============================
		// 🛠️ Helper Methods
		// =============================
		private List<string> GetPayloadKeys(JsonElement element)
		{
			var keys = new List<string>();
			if (element.ValueKind == JsonValueKind.Object)
			{
				foreach (var prop in element.EnumerateObject())
				{
					keys.Add(prop.Name);
				}
			}
			return keys;
		}

		private string GetPropertySafe(JsonElement element, string propertyName)
		{
			try
			{
				if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
				{
					return prop.GetString() ?? "";
				}
				return "";
			}
			catch
			{
				return "";
			}
		}

		private string GetNestedPropertySafe(JsonElement element, string parent, string child)
		{
			try
			{
				if (element.TryGetProperty(parent, out var parentProp) &&
					parentProp.ValueKind == JsonValueKind.Object &&
					parentProp.TryGetProperty(child, out var childProp) &&
					childProp.ValueKind != JsonValueKind.Null)
				{
					return childProp.GetString() ?? "";
				}
				return "";
			}
			catch
			{
				return "";
			}
		}

		private string MapClickUpStatus(string clickUpStatus)
		{
			var mapped = clickUpStatus?.ToLower() switch
			{
				"to do" => "To Do",
				"in progress" => "In Progress",
				"complete" => "Completed",
				"closed" => "Completed",
				"review" => "In Progress",
				_ => "To Do"
			};
			_logger.LogDebug($"🔄 [MAP] Status mapping: '{clickUpStatus}' → '{mapped}'");
			return mapped;
		}

		private int CalculateProgress(string status)
		{
			var progress = status?.ToLower() switch
			{
				"to do" => 0,
				"in progress" => 50,
				"review" => 75,
				"complete" => 100,
				"closed" => 100,
				_ => 0
			};
			_logger.LogDebug($"📈 [MAP] Progress calculation: '{status}' → {progress}%");
			return progress;
		}

		private string ParseClickUpDate(string? dueDate)
		{
			if (string.IsNullOrEmpty(dueDate))
			{
				var defaultDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
				_logger.LogDebug($"⏰ [DATE] No due date, using default: {defaultDate}");
				return defaultDate;
			}

			if (long.TryParse(dueDate, out long timestamp))
			{
				var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
				var formattedDate = date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
				_logger.LogDebug($"⏰ [DATE] Parsed timestamp {dueDate} → {formattedDate}");
				return formattedDate;
			}

			var fallbackDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
			_logger.LogDebug($"⏰ [DATE] Invalid format, using fallback: {fallbackDate}");
			return fallbackDate;
		}
	}
}