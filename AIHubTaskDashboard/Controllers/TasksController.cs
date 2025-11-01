using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	public class TasksController : Controller
	{
		private readonly ApiClientService _api;
		private readonly ClickUpApiService _clickUp;
		private readonly ILogger<TasksController> _logger;

		// 🔥 Cache tags to avoid multiple syncs
		private static Dictionary<string, List<string>>? _tagsCache = null;
		private static DateTime _tagsCacheTime = DateTime.MinValue;
		private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

		public TasksController(
			ApiClientService api,
			ClickUpApiService clickUp,
			ILogger<TasksController> logger)
		{
			_api = api;
			_clickUp = clickUp;
			_logger = logger;
		}

		private async Task<JsonElement> GetUsersFromLocalApi()
		{
			try
			{
				_logger.LogInformation("🔄 [USERS] Fetching from LOCAL UsersController...");

				using var httpClient = new HttpClient();
				var request = HttpContext.Request;
				var baseUrl = $"{request.Scheme}://{request.Host}/";

				httpClient.BaseAddress = new Uri(baseUrl);
				httpClient.Timeout = TimeSpan.FromSeconds(15);

				var response = await httpClient.GetAsync("api/v1/users");
				var usersRes = await response.Content.ReadAsStringAsync();

				if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(usersRes))
				{
					var users = JsonDocument.Parse(usersRes).RootElement;

					if (users.ValueKind == JsonValueKind.Array)
					{
						_logger.LogInformation($"✅ [USERS] Got {users.GetArrayLength()} users from LOCAL API");
						return users;
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [USERS] Exception: {ex.Message}");
			}

			return JsonDocument.Parse("[]").RootElement;
		}

		// 🔥 NEW: Collect all unique tags from tasks
		private HashSet<string> CollectAllTagsFromTasks(Dictionary<string, List<string>> taskTagsDict)
		{
			var allTags = new HashSet<string>();
			foreach (var tagsList in taskTagsDict.Values)
			{
				foreach (var tag in tagsList)
				{
					allTags.Add(tag);
				}
			}
			return allTags;
		}

		// 🔥 Clear tags cache (sau khi create/update/delete task)
		private void ClearTagsCache()
		{
			_tagsCache = null;
			_tagsCacheTime = DateTime.MinValue;
			_logger.LogDebug("🗑️ [CACHE] Tags cache cleared");
		}

		// 🔥 Sync tags từ ClickUp về Dashboard (OPTIMIZED - Parallel + Limit)
		private async Task<Dictionary<string, List<string>>> SyncTaskTagsFromClickUp(JsonElement tasks)
		{
			// Check cache first
			if (_tagsCache != null && DateTime.Now - _tagsCacheTime < CacheExpiry)
			{
				_logger.LogInformation($"✅ [SYNC] Using cached tags ({_tagsCache.Count} tasks)");
				return _tagsCache;
			}

			var taskTags = new Dictionary<string, List<string>>();

			if (tasks.ValueKind != JsonValueKind.Array)
				return taskTags;

			try
			{
				_logger.LogInformation("🏷️ [SYNC] Syncing tags from ClickUp (parallel mode)...");

				// 🔥 Get valid ClickUp IDs (filter out PENDING)
				var validClickUpIds = tasks.EnumerateArray()
					.Select(t => t.TryGetProperty("clickup_id", out var cidProp) ? cidProp.GetString() : null)
					.Where(id => !string.IsNullOrEmpty(id) && !id.StartsWith("PENDING_"))
					.Take(50) // 🔥 LIMIT to 50 tasks max for speed
					.ToList();

				if (validClickUpIds.Count == 0)
				{
					_logger.LogInformation("ℹ️ [SYNC] No valid ClickUp IDs found");
					return taskTags;
				}

				_logger.LogInformation($"🔄 [SYNC] Processing {validClickUpIds.Count} tasks in parallel...");

				// 🔥 Parallel fetch with throttling (10 concurrent max)
				var semaphore = new SemaphoreSlim(10, 10);
				var fetchTasks = validClickUpIds.Select(async clickupId =>
				{
					await semaphore.WaitAsync();
					try
					{
						var tags = await _clickUp.GetTaskTagsAsync(clickupId!);
						return new { ClickUpId = clickupId, Tags = tags };
					}
					finally
					{
						semaphore.Release();
					}
				});

				var results = await Task.WhenAll(fetchTasks);

				// Build dictionary
				foreach (var result in results)
				{
					if (result.Tags.Count > 0)
					{
						taskTags[result.ClickUpId!] = result.Tags;
					}
				}

				_logger.LogInformation($"✅ [SYNC] Synced tags for {taskTags.Count} tasks (parallel)");

				// Update cache
				_tagsCache = taskTags;
				_tagsCacheTime = DateTime.Now;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [SYNC] Error syncing tags: {ex.Message}");
			}

			return taskTags;
		}

		public async Task<IActionResult> Index(string? status, int? assignee_id, int? assigner_id, string? search, string? sort_by, string? sort_order, string? tag_filter, bool skip_tags = false)
		{
			try
			{
				string endpoint = "api/v1/tasks";
				var query = new List<string>();

				// Build query parameters
				if (!string.IsNullOrEmpty(status))
				{
					query.Add($"status={status}");
				}

				if (query.Count > 0)
				{
					endpoint += "?" + string.Join("&", query);
				}

				_logger.LogInformation($"🔍 [INDEX] Fetching tasks with filters: {endpoint}");

				// Fetch tasks from API
				var res = await _api.GetAsync(endpoint);

				JsonElement tasks;
				if (string.IsNullOrWhiteSpace(res))
				{
					tasks = JsonDocument.Parse("[]").RootElement;
				}
				else
				{
					tasks = JsonDocument.Parse(res).RootElement;
					if (tasks.ValueKind != JsonValueKind.Array)
						tasks = JsonDocument.Parse($"[{res}]").RootElement;
				}

				// Client-side filtering for assignee_id
				if (assignee_id.HasValue && assignee_id.Value != 0 && tasks.ValueKind == JsonValueKind.Array)
				{
					var filteredTasks = tasks.EnumerateArray()
						.Where(t => t.TryGetProperty("assignee_id", out var a) && a.GetInt32() == assignee_id.Value)
						.ToList();

					if (filteredTasks.Count != tasks.GetArrayLength())
					{
						_logger.LogInformation($"🔍 [INDEX] Client-side filter by assignee_id={assignee_id}: {tasks.GetArrayLength()} -> {filteredTasks.Count} tasks");
						tasks = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(filteredTasks)).RootElement;
					}
				}

				// Client-side filtering for assigner_id
				if (assigner_id.HasValue && assigner_id.Value != 0 && tasks.ValueKind == JsonValueKind.Array)
				{
					var filteredTasks = tasks.EnumerateArray()
						.Where(t => t.TryGetProperty("assigner_id", out var aid) && aid.GetInt32() == assigner_id.Value)
						.ToList();

					if (filteredTasks.Count != tasks.GetArrayLength())
					{
						_logger.LogInformation($"🔍 [INDEX] Client-side filter by assigner_id={assigner_id}: {tasks.GetArrayLength()} -> {filteredTasks.Count} tasks");
						tasks = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(filteredTasks)).RootElement;
					}
				}

				// 🔥 Sync tags from ClickUp (OPTIONAL - skip for fast load)
				Dictionary<string, List<string>> taskTagsDict;
				HashSet<string> allTagsSet;

				if (skip_tags)
				{
					_logger.LogInformation("⚡ [INDEX] Skipping tags sync for fast load");
					taskTagsDict = new Dictionary<string, List<string>>();
					allTagsSet = new HashSet<string>();
				}
				else
				{
					taskTagsDict = await SyncTaskTagsFromClickUp(tasks);
					allTagsSet = CollectAllTagsFromTasks(taskTagsDict);
					_logger.LogInformation($"📊 [INDEX] Found {allTagsSet.Count} unique tags across all tasks");
				}

				ViewBag.TaskTags = taskTagsDict;
				ViewBag.AllTags = allTagsSet.OrderBy(t => t).ToList();

				// 🔥 Filter by tag if specified
				if (!string.IsNullOrWhiteSpace(tag_filter) && tasks.ValueKind == JsonValueKind.Array)
				{
					var filteredTasks = tasks.EnumerateArray()
						.Where(t => {
							if (!t.TryGetProperty("clickup_id", out var cidProp))
								return false;

							var clickupId = cidProp.GetString();
							if (string.IsNullOrEmpty(clickupId))
								return false;

							if (taskTagsDict.TryGetValue(clickupId, out var tags))
							{
								return tags.Any(tag => tag.Equals(tag_filter, StringComparison.OrdinalIgnoreCase));
							}
							return false;
						})
						.ToList();

					_logger.LogInformation($"🏷️ [INDEX] Filter by tag '{tag_filter}': {tasks.GetArrayLength()} -> {filteredTasks.Count} tasks");
					tasks = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(filteredTasks)).RootElement;
				}

				// Search functionality
				if (!string.IsNullOrWhiteSpace(search) && tasks.ValueKind == JsonValueKind.Array)
				{
					var searchLower = search.ToLower().Trim();
					var searchTerms = searchLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

					var searchResults = tasks.EnumerateArray()
						.Where(t =>
						{
							var title = t.TryGetProperty("title", out var titleProp) ? titleProp.GetString()?.ToLower() : "";
							var description = t.TryGetProperty("description", out var descProp) ? descProp.GetString()?.ToLower() : "";
							var clickupId = t.TryGetProperty("clickup_id", out var cidProp) ? cidProp.GetString()?.ToLower() : "";

							return searchTerms.Any(term =>
								title?.Contains(term) == true ||
								description?.Contains(term) == true ||
								clickupId?.Contains(term) == true
							);
						})
						.ToList();

					_logger.LogInformation($"🔍 [INDEX] Search '{search}': {tasks.GetArrayLength()} -> {searchResults.Count} tasks");
					tasks = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(searchResults)).RootElement;
				}

				// Sorting functionality
				if (!string.IsNullOrWhiteSpace(sort_by) && tasks.ValueKind == JsonValueKind.Array)
				{
					var tasksList = tasks.EnumerateArray().ToList();
					var isAscending = sort_order?.ToLower() == "asc";

					tasksList = sort_by.ToLower() switch
					{
						"task_id" => isAscending
							? tasksList.OrderBy(t => t.TryGetProperty("task_id", out var id) ? id.GetInt32() : 0).ToList()
							: tasksList.OrderByDescending(t => t.TryGetProperty("task_id", out var id) ? id.GetInt32() : 0).ToList(),

						"title" => isAscending
							? tasksList.OrderBy(t => t.TryGetProperty("title", out var title) ? title.GetString() : "").ToList()
							: tasksList.OrderByDescending(t => t.TryGetProperty("title", out var title) ? title.GetString() : "").ToList(),

						"status" => isAscending
							? tasksList.OrderBy(t => t.TryGetProperty("status", out var status) ? status.GetString() : "").ToList()
							: tasksList.OrderByDescending(t => t.TryGetProperty("status", out var status) ? status.GetString() : "").ToList(),

						"progress" => isAscending
							? tasksList.OrderBy(t => t.TryGetProperty("progress_percentage", out var prog) ? prog.GetInt32() : 0).ToList()
							: tasksList.OrderByDescending(t => t.TryGetProperty("progress_percentage", out var prog) ? prog.GetInt32() : 0).ToList(),

						"deadline" => isAscending
							? tasksList.OrderBy(t => {
								if (t.TryGetProperty("deadline", out var dl) && DateTime.TryParse(dl.GetString(), out var date))
									return date;
								return DateTime.MaxValue;
							}).ToList()
							: tasksList.OrderByDescending(t => {
								if (t.TryGetProperty("deadline", out var dl) && DateTime.TryParse(dl.GetString(), out var date))
									return date;
								return DateTime.MinValue;
							}).ToList(),

						"created_at" => isAscending
							? tasksList.OrderBy(t => {
								if (t.TryGetProperty("created_at", out var ca) && DateTime.TryParse(ca.GetString(), out var date))
									return date;
								return DateTime.MaxValue;
							}).ToList()
							: tasksList.OrderByDescending(t => {
								if (t.TryGetProperty("created_at", out var ca) && DateTime.TryParse(ca.GetString(), out var date))
									return date;
								return DateTime.MinValue;
							}).ToList(),

						_ => tasksList
					};

					_logger.LogInformation($"📊 [INDEX] Sorted by {sort_by} ({sort_order})");
					tasks = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(tasksList)).RootElement;
				}

				ViewBag.Users = await GetUsersFromLocalApi();
				return View(tasks);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [INDEX] Error: {ex.Message}");
				ViewBag.Users = JsonDocument.Parse("[]").RootElement;
				ViewBag.TaskTags = new Dictionary<string, List<string>>();
				ViewBag.AllTags = new List<string>();
				var emptyJson = JsonDocument.Parse("[]").RootElement;
				return View(emptyJson);
			}
		}


		[HttpGet]
		public async Task<IActionResult> Create()
		{
			try
			{
				_logger.LogInformation("🔄 [CREATE] Loading Create page...");
				ViewBag.Users = await GetUsersFromLocalApi();

				// 🔥 Get tags from existing tasks instead of Space API
				var tasksRes = await _api.GetAsync("api/v1/tasks");
				JsonElement tasks;
				if (string.IsNullOrWhiteSpace(tasksRes))
				{
					tasks = JsonDocument.Parse("[]").RootElement;
				}
				else
				{
					tasks = JsonDocument.Parse(tasksRes).RootElement;
					if (tasks.ValueKind != JsonValueKind.Array)
						tasks = JsonDocument.Parse($"[{tasksRes}]").RootElement;
				}

				var taskTagsDict = await SyncTaskTagsFromClickUp(tasks);
				var allTags = CollectAllTagsFromTasks(taskTagsDict);
				ViewBag.Tags = allTags.OrderBy(t => t).ToList();

				var emptyJson = JsonDocument.Parse("{}").RootElement;
				return View(emptyJson);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [CREATE] Error: {ex.Message}");
				ViewBag.Users = JsonDocument.Parse("[]").RootElement;
				ViewBag.Tags = new List<string>();
				var emptyJson = JsonDocument.Parse("{}").RootElement;
				return View(emptyJson);
			}
		}

		[HttpPost]
		public async Task<IActionResult> Create(string title, string description, string status, int progress_percentage, int assignee_id, string? tags)
		{
			string userIdString = HttpContext.Session.GetString("id");
			int assigner_id = 0;
			if (!string.IsNullOrEmpty(userIdString))
			{
				int.TryParse(userIdString, out assigner_id);
			}

			if (assigner_id == 0)
			{
				TempData["Error"] = "Người giao không hợp lệ. Vui lòng đăng nhập lại.";
				return RedirectToAction("Create");
			}

			try
			{
				_logger.LogInformation($"➕ [CREATE] Creating task: {title}");
				_logger.LogInformation($"🏷️ [CREATE] Tags: {tags ?? "none"}");

				// Parse tags
				List<string> tagsList = new List<string>();
				if (!string.IsNullOrEmpty(tags))
				{
					tagsList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
						.Select(t => t.Trim())
						.Where(t => !string.IsNullOrEmpty(t))
						.ToList();
				}

				// STEP 1: Create in ClickUp FIRST (with tags)
				string? realClickUpId = null;
				try
				{
					_logger.LogInformation($"📤 [CREATE] Step 1: Creating in ClickUp first");
					realClickUpId = await _clickUp.CreateTaskAsync(title, description, status, assignee_id, tagsList);

					if (string.IsNullOrEmpty(realClickUpId))
					{
						throw new Exception("ClickUp creation returned null or empty ID");
					}

					_logger.LogInformation($"✅ [CREATE] ClickUp task created: {realClickUpId}");
				}
				catch (Exception clickUpEx)
				{
					_logger.LogError($"❌ [CREATE] ClickUp creation failed: {clickUpEx.Message}");
					TempData["Error"] = $"Không thể tạo task trong ClickUp: {clickUpEx.Message}";
					return RedirectToAction("Create");
				}

				// STEP 2: Create in Dashboard with real ClickUp ID
				var collaborators = new List<int> { assignee_id };
				string expected_output = "Chưa có yêu cầu đầu ra cụ thể.";
				string deadline = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

				var payload = new
				{
					clickup_id = realClickUpId,
					title,
					description,
					assigner_id,
					assignee_id,
					collaborators,
					expected_output,
					deadline,
					status,
					progress_percentage,
					notion_link = string.Empty
				};

				_logger.LogInformation($"📤 [CREATE] Step 2: Creating in Dashboard with clickup_id: {realClickUpId}");

				var dashboardResponse = await _api.PostAsync("api/v1/tasks", payload);

				if (string.IsNullOrEmpty(dashboardResponse))
				{
					// Dashboard failed, need to delete ClickUp task
					_logger.LogError("❌ [CREATE] Dashboard creation failed, rolling back ClickUp task");
					try
					{
						await _clickUp.DeleteTaskAsync(realClickUpId);
					}
					catch (Exception rollbackEx)
					{
						_logger.LogError($"❌ [CREATE] Rollback failed: {rollbackEx.Message}");
					}

					throw new Exception("Dashboard task creation failed");
				}

				var createdTask = JsonDocument.Parse(dashboardResponse).RootElement;
				var dashboardTaskId = createdTask.GetProperty("task_id").GetInt32();

				_logger.LogInformation($"✅ [CREATE] Task creation completed:");
				_logger.LogInformation($"   - Dashboard task ID: {dashboardTaskId}");
				_logger.LogInformation($"   - ClickUp task ID: {realClickUpId}");
				_logger.LogInformation($"   - Tags: {string.Join(", ", tagsList)}");

				ClearTagsCache(); // 🔥 Clear cache after create

				TempData["Success"] = "Task đã được tạo thành công!";
				return RedirectToAction("Index", "Tasks");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [CREATE] Error: {ex.Message}");
				_logger.LogError($"❌ [CREATE] StackTrace: {ex.StackTrace}");
				TempData["Error"] = $"Tạo Task thất bại: {ex.Message}";
				return RedirectToAction("Create");
			}
		}

		[HttpGet]
		public async Task<IActionResult> Edit(int id)
		{
			try
			{
				_logger.LogInformation($"🔄 [EDIT] Loading edit page for task: {id}");

				var res = await _api.GetAsync($"api/v1/tasks/{id}");
				JsonElement task;

				if (string.IsNullOrWhiteSpace(res))
				{
					_logger.LogWarning($"⚠️ [EDIT] Task {id} not found");
					TempData["Error"] = "Task không tồn tại.";
					return RedirectToAction("Index");
				}
				else
				{
					task = JsonDocument.Parse(res).RootElement;
				}

				ViewBag.Users = await GetUsersFromLocalApi();

				// 🔥 Get tags from existing tasks
				var tasksRes = await _api.GetAsync("api/v1/tasks");
				JsonElement tasks;
				if (string.IsNullOrWhiteSpace(tasksRes))
				{
					tasks = JsonDocument.Parse("[]").RootElement;
				}
				else
				{
					tasks = JsonDocument.Parse(tasksRes).RootElement;
					if (tasks.ValueKind != JsonValueKind.Array)
						tasks = JsonDocument.Parse($"[{tasksRes}]").RootElement;
				}

				var taskTagsDict = await SyncTaskTagsFromClickUp(tasks);
				var allTags = CollectAllTagsFromTasks(taskTagsDict);
				ViewBag.Tags = allTags.OrderBy(t => t).ToList();

				return View(task);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [EDIT] Load error: {ex.Message}");
				TempData["Error"] = $"Không thể tải thông tin task: {ex.Message}";
				return RedirectToAction("Index");
			}
		}

		[HttpPost]
		public async Task<IActionResult> Edit(int id, string title, string description, string status, int progress_percentage, int? assignee_id, string? tags)
		{
			try
			{
				_logger.LogInformation($"🔄 [EDIT] Updating task: {id}");
				_logger.LogInformation($"🏷️ [EDIT] Tags: {tags ?? "none"}");

				var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");
				var task = JsonDocument.Parse(taskRes).RootElement;

				// Parse tags
				List<string> tagsList = new List<string>();
				if (!string.IsNullOrEmpty(tags))
				{
					tagsList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
						.Select(t => t.Trim())
						.Where(t => !string.IsNullOrEmpty(t))
						.ToList();
				}

				// Update Dashboard first
				var payload = new
				{
					title,
					description,
					status,
					progress_percentage,
					assignee_id
				};

				_logger.LogInformation($"📤 [EDIT] Updating Dashboard task {id}");
				await _api.PutAsync($"api/v1/tasks/{id}", payload);
				_logger.LogInformation($"✅ [EDIT] Dashboard updated: {id}");

				// Sync to ClickUp if has valid clickup_id
				if (task.TryGetProperty("clickup_id", out var clickupIdProp))
				{
					var clickupId = clickupIdProp.GetString();

					if (!string.IsNullOrEmpty(clickupId) && !clickupId.StartsWith("PENDING_"))
					{
						try
						{
							_logger.LogInformation($"🔄 [EDIT] Syncing to ClickUp: {clickupId}");
							await _clickUp.UpdateTaskAsync(clickupId, title, description, status, assignee_id, tagsList);
							_logger.LogInformation($"✅ [EDIT] ClickUp synced: {clickupId}");
						}
						catch (Exception clickUpEx)
						{
							_logger.LogWarning($"⚠️ [EDIT] ClickUp sync failed: {clickUpEx.Message}");
							TempData["Warning"] = "Task đã cập nhật trong Dashboard nhưng không sync được với ClickUp.";
						}
					}
					else if (clickupId?.StartsWith("PENDING_") == true)
					{
						_logger.LogWarning($"⚠️ [EDIT] Task has placeholder clickup_id, skipping sync: {clickupId}");
						TempData["Warning"] = "Task có placeholder ClickUp ID, không thể sync.";
					}
				}

				ClearTagsCache(); // 🔥 Clear cache after edit

				TempData["Success"] = "Task đã được cập nhật!";
				return RedirectToAction("Index");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [EDIT] Update error: {ex.Message}");
				TempData["Error"] = $"Cập nhật Task thất bại: {ex.Message}";
				return RedirectToAction("Edit", new { id });
			}
		}

		[HttpPost]
		public async Task<IActionResult> Delete(int id)
		{
			try
			{
				_logger.LogInformation($"🗑️ [DELETE] Deleting task: {id}");

				var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");

				if (string.IsNullOrEmpty(taskRes))
				{
					_logger.LogWarning($"⚠️ [DELETE] Task {id} not found");
					TempData["Warning"] = "Task không tồn tại hoặc đã bị xóa.";
					return RedirectToAction("Index");
				}

				var task = JsonDocument.Parse(taskRes).RootElement;
				var taskTitle = task.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : $"Task #{id}";

				if (task.TryGetProperty("clickup_id", out var clickupIdProp))
				{
					var clickupId = clickupIdProp.GetString();

					if (!string.IsNullOrEmpty(clickupId) && !clickupId.StartsWith("PENDING_"))
					{
						try
						{
							_logger.LogInformation($"🗑️ [DELETE] Deleting from ClickUp: {clickupId}");
							await _clickUp.DeleteTaskAsync(clickupId);
							_logger.LogInformation($"✅ [DELETE] Deleted from ClickUp: {clickupId}");
						}
						catch (Exception clickUpEx)
						{
							_logger.LogWarning($"⚠️ [DELETE] ClickUp delete failed: {clickUpEx.Message}");
						}
					}
					else if (clickupId?.StartsWith("PENDING_") == true)
					{
						_logger.LogInformation($"ℹ️ [DELETE] Skipping ClickUp delete for placeholder: {clickupId}");
					}
				}

				await _api.DeleteAsync($"api/v1/tasks/{id}");

				ClearTagsCache(); // 🔥 Clear cache after delete

				_logger.LogInformation($"✅ [DELETE] Task deleted successfully: {id}");
				TempData["Success"] = $"Task \"{taskTitle}\" đã được xóa!";
				return RedirectToAction("Index");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DELETE] Delete error: {ex.Message}");
				_logger.LogError($"❌ [DELETE] StackTrace: {ex.StackTrace}");
				TempData["Error"] = $"Xóa Task thất bại: {ex.Message}";
				return RedirectToAction("Index");
			}
		}
	}
}