using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	public class KPIController : Controller
	{
		private readonly ApiClientService _api;
		private readonly ClickUpApiService _clickUp;
		private readonly ILogger<KPIController> _logger;

		// 🔥 Fixed tags - chỉ 3 tags này thôi
		private static readonly List<string> FIXED_TAGS = new() { "admin", "content", "dev" };

		// 🔥 Cache tối ưu
		private static Dictionary<string, List<string>>? _tagsCache = null;
		private static DateTime _tagsCacheTime = DateTime.MinValue;
		private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(10);

		// 🔥 Cache KPI data để load siêu nhanh
		private static Dictionary<string, TeamKPIData>? _kpiCache = null;
		private static DateTime _kpiCacheTime = DateTime.MinValue;

		public KPIController(
			ApiClientService api,
			ClickUpApiService clickUp,
			ILogger<KPIController> logger)
		{
			_api = api;
			_clickUp = clickUp;
			_logger = logger;
		}

		public async Task<IActionResult> Index(string? team)
		{
			try
			{
				_logger.LogInformation($"📊 [KPI] Loading KPI Dashboard (team: {team})");

				// 🔥 Check cache trước - nếu có thì trả luôn, siêu nhanh
				if (_kpiCache != null && DateTime.Now - _kpiCacheTime < CacheExpiry)
				{
					_logger.LogInformation($"⚡ [KPI] Using cached KPI data");

					var cachedModel = new KPIViewModel
					{
						CurrentTeam = team ?? "admin",
						AllTeams = FIXED_TAGS,
						TeamKPIs = _kpiCache
					};

					return View(cachedModel);
				}

				// 🔥 Load song song tasks và users để nhanh hơn
				var tasksTask = LoadAllTasksAsync();
				var usersTask = GetUsersFromLocalApi();

				await Task.WhenAll(tasksTask, usersTask);

				var allTasks = await tasksTask;
				var allUsers = await usersTask;

				_logger.LogInformation($"✅ [KPI] Loaded {allTasks.GetArrayLength()} tasks, {allUsers.GetArrayLength()} users");

				// 🔥 DEBUG: Log first task structure
				if (allTasks.GetArrayLength() > 0)
				{
					var firstTask = allTasks.EnumerateArray().First();
					_logger.LogInformation($"🔍 [KPI] First task structure: {JsonSerializer.Serialize(firstTask, new JsonSerializerOptions { WriteIndented = false })}");
				}

				// 🔥 Sync tags (có cache)
				var taskTagsDict = await SyncTaskTagsFromClickUp(allTasks);

				// 🔥 Tính KPI cho 3 tags cố định
				var teamKPIs = new Dictionary<string, TeamKPIData>();

				foreach (var tag in FIXED_TAGS)
				{
					var filteredTasks = FilterTasksByTag(allTasks, taskTagsDict, tag);
					_logger.LogInformation($"🔍 [KPI] Tag '{tag}': {filteredTasks.Count} tasks");

					var kpiData = CalculateKPIForTeam(filteredTasks, allUsers, tag);
					teamKPIs[tag] = kpiData;

					_logger.LogInformation($"📈 [KPI] Team '{tag}' KPIs: Total={kpiData.TotalTasks}, ToDo={kpiData.ToDoTasks}, InProgress={kpiData.InProgressTasks}, Completed={kpiData.CompletedTasks}, Overdue={kpiData.OverdueTasks}");
				}

				// 🔥 Cache KPI data
				_kpiCache = teamKPIs;
				_kpiCacheTime = DateTime.Now;

				var model = new KPIViewModel
				{
					CurrentTeam = team ?? "admin",
					AllTeams = FIXED_TAGS,
					TeamKPIs = teamKPIs
				};

				_logger.LogInformation($"📈 [KPI] Done - admin: {teamKPIs["admin"].TotalTasks}, content: {teamKPIs["content"].TotalTasks}, dev: {teamKPIs["dev"].TotalTasks}");

				return View(model);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [KPI] Fatal error: {ex.Message}");
				_logger.LogError($"❌ [KPI] Exception type: {ex.GetType().Name}");
				_logger.LogError($"❌ [KPI] StackTrace: {ex.StackTrace}");

				if (ex.InnerException != null)
				{
					_logger.LogError($"❌ [KPI] Inner exception: {ex.InnerException.Message}");
				}

				var emptyModel = new KPIViewModel
				{
					CurrentTeam = team ?? "admin",
					AllTeams = FIXED_TAGS
				};

				return View(emptyModel);
			}
		}

		// 🔥 Load tasks với timeout ngắn và error handling tốt hơn
		private async Task<JsonElement> LoadAllTasksAsync()
		{
			try
			{
				_logger.LogInformation("🔄 [KPI] Loading tasks...");
				var tasksRes = await _api.GetAsync("api/v1/tasks");

				if (string.IsNullOrWhiteSpace(tasksRes))
				{
					_logger.LogWarning("⚠️ [KPI] API returned empty response");
					return JsonDocument.Parse("[]").RootElement;
				}

				_logger.LogInformation($"📥 [KPI] Raw response length: {tasksRes.Length} chars");

				try
				{
					var tasks = JsonDocument.Parse(tasksRes).RootElement;

					if (tasks.ValueKind != JsonValueKind.Array)
					{
						_logger.LogWarning($"⚠️ [KPI] Response is not array, wrapping it. ValueKind: {tasks.ValueKind}");
						return JsonDocument.Parse($"[{tasksRes}]").RootElement;
					}

					_logger.LogInformation($"✅ [KPI] Parsed {tasks.GetArrayLength()} tasks");
					return tasks;
				}
				catch (JsonException jsonEx)
				{
					_logger.LogError($"❌ [KPI] JSON parse error: {jsonEx.Message}");
					_logger.LogError($"📥 [KPI] Invalid JSON (first 500 chars): {tasksRes.Substring(0, Math.Min(500, tasksRes.Length))}");
					return JsonDocument.Parse("[]").RootElement;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [KPI] Error loading tasks: {ex.Message}");
				return JsonDocument.Parse("[]").RootElement;
			}
		}

		// 🔥 Sync tags với cache và parallel processing
		private async Task<Dictionary<string, List<string>>> SyncTaskTagsFromClickUp(JsonElement tasks)
		{
			if (_tagsCache != null && DateTime.Now - _tagsCacheTime < CacheExpiry)
			{
				_logger.LogInformation($"✅ [KPI] Using cached tags ({_tagsCache.Count} tasks)");
				return _tagsCache;
			}

			var taskTags = new Dictionary<string, List<string>>();

			if (tasks.ValueKind != JsonValueKind.Array)
			{
				_logger.LogWarning($"⚠️ [KPI] Tasks is not array for tag sync, ValueKind: {tasks.ValueKind}");
				return taskTags;
			}

			try
			{
				var validClickUpIds = tasks.EnumerateArray()
					.Select(t => t.TryGetProperty("clickup_id", out var cidProp) ? cidProp.GetString() : null)
					.Where(id => !string.IsNullOrEmpty(id) && !id.StartsWith("PENDING_"))
					.Take(150) // Tăng lên 150 để đủ data
					.ToList();

				if (validClickUpIds.Count == 0)
				{
					_logger.LogInformation("ℹ️ [KPI] No valid ClickUp IDs");
					return taskTags;
				}

				_logger.LogInformation($"🔄 [KPI] Syncing tags for {validClickUpIds.Count} tasks...");

				// 🔥 Parallel với semaphore = 15 để nhanh hơn
				var semaphore = new SemaphoreSlim(15, 15);
				var fetchTasks = validClickUpIds.Select(async clickupId =>
				{
					await semaphore.WaitAsync();
					try
					{
						var tags = await _clickUp.GetTaskTagsAsync(clickupId!);
						return new { ClickUpId = clickupId, Tags = tags };
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [KPI] Failed to get tags for {clickupId}: {ex.Message}");
						return new { ClickUpId = clickupId, Tags = new List<string>() };
					}
					finally
					{
						semaphore.Release();
					}
				});

				var results = await Task.WhenAll(fetchTasks);

				foreach (var result in results)
				{
					if (result.Tags.Count > 0)
					{
						taskTags[result.ClickUpId!] = result.Tags;
					}
				}

				_logger.LogInformation($"✅ [KPI] Synced tags for {taskTags.Count} tasks");

				// 🔥 DEBUG: Show tag distribution
				var tagCounts = new Dictionary<string, int>();
				foreach (var tags in taskTags.Values)
				{
					foreach (var tag in tags)
					{
						tagCounts[tag] = tagCounts.GetValueOrDefault(tag, 0) + 1;
					}
				}
				_logger.LogInformation($"📊 [KPI] Tag distribution: {string.Join(", ", tagCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");

				_tagsCache = taskTags;
				_tagsCacheTime = DateTime.Now;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [KPI] Error syncing tags: {ex.Message}");
			}

			return taskTags;
		}

		private async Task<JsonElement> GetUsersFromLocalApi()
		{
			try
			{
				_logger.LogInformation("🔄 [KPI] Loading users...");

				using var httpClient = new HttpClient();
				var request = HttpContext.Request;
				var baseUrl = $"{request.Scheme}://{request.Host}/";
				httpClient.BaseAddress = new Uri(baseUrl);
				httpClient.Timeout = TimeSpan.FromSeconds(10);

				var response = await httpClient.GetAsync("api/v1/users");
				var usersRes = await response.Content.ReadAsStringAsync();

				if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(usersRes))
				{
					var users = JsonDocument.Parse(usersRes).RootElement;

					if (users.ValueKind == JsonValueKind.Array)
					{
						_logger.LogInformation($"✅ [KPI] Loaded {users.GetArrayLength()} users");
						return users;
					}
				}

				_logger.LogWarning("⚠️ [KPI] Failed to load users or invalid format");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [KPI] Error loading users: {ex.Message}");
			}

			return JsonDocument.Parse("[]").RootElement;
		}

		// 🔥 Filter nhanh với LINQ tối ưu
		private List<JsonElement> FilterTasksByTag(
			JsonElement tasks,
			Dictionary<string, List<string>> taskTagsDict,
			string targetTag)
		{
			if (tasks.ValueKind != JsonValueKind.Array)
			{
				_logger.LogWarning($"⚠️ [KPI] FilterTasksByTag: tasks is not array, ValueKind: {tasks.ValueKind}");
				return new List<JsonElement>();
			}

			var filtered = tasks.EnumerateArray()
				.Where(task =>
				{
					if (!task.TryGetProperty("clickup_id", out var cidProp))
						return false;

					var clickupId = cidProp.GetString();
					if (string.IsNullOrEmpty(clickupId))
						return false;

					return taskTagsDict.TryGetValue(clickupId, out var tags) &&
						   tags.Any(t => t.Equals(targetTag, StringComparison.OrdinalIgnoreCase));
				})
				.ToList();

			return filtered;
		}

		private TeamKPIData CalculateKPIForTeam(
			List<JsonElement> tasks,
			JsonElement users,
			string team)
		{
			var model = new TeamKPIData { TeamName = team, TotalTasks = tasks.Count };

			if (tasks.Count == 0)
			{
				_logger.LogInformation($"ℹ️ [KPI-CALC] Team '{team}' has no tasks");
				return model;
			}

			_logger.LogInformation($"📊 [KPI-CALC] Calculating KPI for team '{team}' with {tasks.Count} tasks");

			// User mapping
			var userMap = new Dictionary<int, string>();
			if (users.ValueKind == JsonValueKind.Array)
			{
				foreach (var user in users.EnumerateArray())
				{
					if (user.TryGetProperty("id", out var userId))
					{
						var id = userId.GetInt32();
						var name = user.TryGetProperty("name", out var userName)
							? userName.GetString()
							: $"User #{id}";

						userMap[id] = name ?? $"User #{id}";
					}
				}
				_logger.LogInformation($"📋 [KPI-CALC] Mapped {userMap.Count} users");
			}

			var memberStats = new Dictionary<int, TeamMemberKPI>();
			var now = DateTime.Now;
			var taskCounter = 0;

			foreach (var task in tasks)
			{
				taskCounter++;
				var taskId = task.TryGetProperty("task_id", out var tid) ? tid.GetInt32() : 0;

				// 🔥 Get status - NORMALIZED (case insensitive, no spaces/underscores)
				var rawStatus = task.TryGetProperty("status", out var statusProp)
					? statusProp.GetString()
					: "";

				var status = rawStatus?.ToLower()?.Trim().Replace(" ", "").Replace("_", "") ?? "";

				// 🔥 DEBUG first 3 tasks
				if (taskCounter <= 3)
				{
					_logger.LogInformation($"🔍 [KPI-CALC] Task #{taskCounter} (ID={taskId}): Status raw='{rawStatus}', normalized='{status}'");
				}

				// 🔥 Count status - NORMALIZED
				if (status == "todo" || status == "pending" || status == "notstarted")
				{
					model.ToDoTasks++;
				}
				else if (status == "inprogress" || status == "ongoing" || status == "active")
				{
					model.InProgressTasks++;
				}
				else if (status == "completed" || status == "done" || status == "complete" || status == "finished")
				{
					model.CompletedTasks++;
				}
				else if (!string.IsNullOrEmpty(status))
				{
					_logger.LogWarning($"⚠️ [KPI-CALC] Unknown status: '{status}' (raw: '{rawStatus}') for task {taskId}");
				}

				// Check overdue
				var isOverdue = false;
				var isCompletedStatus = status == "completed" || status == "done" || status == "complete" || status == "finished";

				if (task.TryGetProperty("deadline", out var deadlineProp) && !isCompletedStatus)
				{
					try
					{
						var deadlineStr = deadlineProp.GetString();
						if (!string.IsNullOrEmpty(deadlineStr) &&
							DateTime.TryParse(deadlineStr, out var deadline) &&
							deadline < now)
						{
							isOverdue = true;
							model.OverdueTasks++;
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [KPI-CALC] Error parsing deadline for task {taskId}: {ex.Message}");
					}
				}

				// Track by assignee
				if (task.TryGetProperty("assignee_id", out var assigneeIdProp))
				{
					try
					{
						var assigneeId = assigneeIdProp.GetInt32();

						if (assigneeId > 0)
						{
							if (!memberStats.ContainsKey(assigneeId))
							{
								memberStats[assigneeId] = new TeamMemberKPI
								{
									UserId = assigneeId,
									UserName = userMap.GetValueOrDefault(assigneeId, $"User #{assigneeId}")
								};
							}

							var member = memberStats[assigneeId];
							member.TotalTasks++;

							// 🔥 Count by status - NORMALIZED
							if (status == "todo" || status == "pending" || status == "notstarted")
							{
								member.ToDoTasks++;
							}
							else if (status == "inprogress" || status == "ongoing" || status == "active")
							{
								member.InProgressTasks++;
							}
							else if (status == "completed" || status == "done" || status == "complete" || status == "finished")
							{
								member.CompletedTasks++;
							}

							if (isOverdue)
								member.OverdueTasks++;

							if (task.TryGetProperty("progress_percentage", out var progressProp))
							{
								try
								{
									member.TotalProgress += progressProp.ValueKind == JsonValueKind.Number
										? progressProp.GetInt32()
										: 0;
								}
								catch (Exception ex)
								{
									_logger.LogWarning($"⚠️ [KPI-CALC] Error parsing progress for task {taskId}: {ex.Message}");
								}
							}
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [KPI-CALC] Error processing assignee for task {taskId}: {ex.Message}");
					}
				}
			}

			_logger.LogInformation($"📊 [KPI-CALC] Team '{team}' status breakdown: ToDo={model.ToDoTasks}, InProgress={model.InProgressTasks}, Completed={model.CompletedTasks}, Overdue={model.OverdueTasks}");

			// Completion rate
			if (model.TotalTasks > 0)
			{
				model.CompletionRate = (int)Math.Round(
					(double)model.CompletedTasks / model.TotalTasks * 100);
			}

			// Average progress
			foreach (var member in memberStats.Values)
			{
				if (member.TotalTasks > 0)
					member.AverageProgress = member.TotalProgress / member.TotalTasks;
			}

			model.TeamMembers = memberStats.Values
				.OrderByDescending(m => m.CompletionRate)
				.ThenByDescending(m => m.TotalTasks)
				.ToList();

			_logger.LogInformation($"👥 [KPI-CALC] Team '{team}' has {model.TeamMembers.Count} members");

			return model;
		}
	}

	public class KPIViewModel
	{
		public string CurrentTeam { get; set; } = "admin";
		public List<string> AllTeams { get; set; } = new();
		public Dictionary<string, TeamKPIData> TeamKPIs { get; set; } = new();

		public TeamKPIData? GetTeamKPI(string team)
		{
			return TeamKPIs.GetValueOrDefault(team);
		}
	}

	public class TeamKPIData
	{
		public string TeamName { get; set; } = "";
		public int TotalTasks { get; set; }
		public int ToDoTasks { get; set; }
		public int InProgressTasks { get; set; }
		public int CompletedTasks { get; set; }
		public int OverdueTasks { get; set; }
		public int CompletionRate { get; set; }
		public List<TeamMemberKPI> TeamMembers { get; set; } = new();
	}

	public class TeamMemberKPI
	{
		public int UserId { get; set; }
		public string UserName { get; set; } = "Unknown";
		public int TotalTasks { get; set; }
		public int ToDoTasks { get; set; }
		public int InProgressTasks { get; set; }
		public int CompletedTasks { get; set; }
		public int OverdueTasks { get; set; }
		public int TotalProgress { get; set; }
		public int AverageProgress { get; set; }

		public int CompletionRate => TotalTasks > 0
			? (int)Math.Round((double)CompletedTasks / TotalTasks * 100)
			: 0;
	}
}