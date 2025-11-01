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
				_logger.LogError($"❌ [KPI] Error: {ex.Message}");

				var emptyModel = new KPIViewModel
				{
					CurrentTeam = team ?? "admin",
					AllTeams = FIXED_TAGS
				};

				return View(emptyModel);
			}
		}

		// 🔥 Load tasks với timeout ngắn
		private async Task<JsonElement> LoadAllTasksAsync()
		{
			try
			{
				var tasksRes = await _api.GetAsync("api/v1/tasks");

				if (string.IsNullOrWhiteSpace(tasksRes))
					return JsonDocument.Parse("[]").RootElement;

				var tasks = JsonDocument.Parse(tasksRes).RootElement;

				if (tasks.ValueKind != JsonValueKind.Array)
					return JsonDocument.Parse($"[{tasksRes}]").RootElement;

				return tasks;
			}
			catch
			{
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
				return taskTags;

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
					catch
					{
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
					_logger.LogInformation($"✅ [KPI] Loaded {users.GetArrayLength()} users");
					return users;
				}
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
				return new List<JsonElement>();

			return tasks.EnumerateArray()
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
		}

		private TeamKPIData CalculateKPIForTeam(
			List<JsonElement> tasks,
			JsonElement users,
			string team)
		{
			var model = new TeamKPIData { TeamName = team, TotalTasks = tasks.Count };

			if (tasks.Count == 0)
				return model;

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
			}

			var memberStats = new Dictionary<int, TeamMemberKPI>();
			var now = DateTime.Now;

			foreach (var task in tasks)
			{
				var status = task.TryGetProperty("status", out var statusProp)
					? statusProp.GetString()?.ToLower() ?? ""
					: "";

				// Count status
				switch (status)
				{
					case "to do":
					case "pending":
						model.ToDoTasks++;
						break;
					case "in progress":
						model.InProgressTasks++;
						break;
					case "completed":
					case "done":
						model.CompletedTasks++;
						break;
				}

				// Check overdue
				var isOverdue = false;
				if (task.TryGetProperty("deadline", out var deadlineProp) &&
					status != "completed" && status != "done")
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

				// Track by assignee
				if (task.TryGetProperty("assignee_id", out var assigneeIdProp))
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

						switch (status)
						{
							case "to do":
							case "pending":
								member.ToDoTasks++;
								break;
							case "in progress":
								member.InProgressTasks++;
								break;
							case "completed":
							case "done":
								member.CompletedTasks++;
								break;
						}

						if (isOverdue)
							member.OverdueTasks++;

						if (task.TryGetProperty("progress_percentage", out var progressProp))
							member.TotalProgress += progressProp.GetInt32();
					}
				}
			}

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