using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using AIHubTaskDashboard.ViewModel;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	public class HomeController : Controller
	{
		private readonly ApiClientService _api;
		private readonly ILogger<HomeController> _logger;

		public HomeController(ApiClientService api, ILogger<HomeController> logger)
		{
			_api = api;
			_logger = logger;
		}

		private async Task<JsonElement> GetUsersFromLocalApi()
		{
			try
			{
				_logger.LogInformation("🔄 [DASHBOARD-USERS] Fetching from LOCAL UsersController...");

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
						_logger.LogInformation($"✅ [DASHBOARD-USERS] Got {users.GetArrayLength()} users from LOCAL API");
						return users;
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DASHBOARD-USERS] Exception: {ex.Message}");
			}

			return JsonDocument.Parse("[]").RootElement;
		}

		public async Task<IActionResult> Index()
		{
			try
			{
				// Check if user is logged in
				var token = HttpContext.Session.GetString("AuthToken");
				if (string.IsNullOrEmpty(token))
				{
					return RedirectToAction("Login", "Account");
				}

				_logger.LogInformation("🏠 [DASHBOARD] Loading dashboard...");

				// 🔥 Fetch tasks from API (GIỐNG TASKSCONTROLLER)
				string endpoint = "api/v1/tasks";
				_logger.LogInformation($"🔍 [DASHBOARD] Fetching tasks from: {endpoint}");

				var res = await _api.GetAsync(endpoint);

				JsonElement tasks;
				if (string.IsNullOrWhiteSpace(res))
				{
					_logger.LogWarning("⚠️ [DASHBOARD] API returned empty response");
					tasks = JsonDocument.Parse("[]").RootElement;
				}
				else
				{
					try
					{
						_logger.LogInformation($"📥 [DASHBOARD] Raw response length: {res.Length} chars");

						// 🔥 FIX: Parse và kiểm tra định dạng giống TasksController
						var parsedJson = JsonDocument.Parse(res);
						tasks = parsedJson.RootElement;

						// 🔥 Nếu không phải array, wrap nó trong array
						if (tasks.ValueKind != JsonValueKind.Array)
						{
							_logger.LogWarning($"⚠️ [DASHBOARD] Response is not array, wrapping it. ValueKind: {tasks.ValueKind}");
							tasks = JsonDocument.Parse($"[{res}]").RootElement;
						}

						_logger.LogInformation($"✅ [DASHBOARD] Parsed {tasks.GetArrayLength()} tasks");
					}
					catch (JsonException jsonEx)
					{
						_logger.LogError($"❌ [DASHBOARD] JSON parse error: {jsonEx.Message}");
						_logger.LogError($"📥 [DASHBOARD] Invalid JSON (first 500 chars): {res.Substring(0, Math.Min(500, res.Length))}");
						tasks = JsonDocument.Parse("[]").RootElement;
					}
				}

				// 🔥 DEBUG: Log task structure
				if (tasks.GetArrayLength() > 0)
				{
					var firstTask = tasks.EnumerateArray().First();
					_logger.LogInformation($"🔍 [DASHBOARD] First task structure:");
					_logger.LogInformation($"   Raw JSON: {JsonSerializer.Serialize(firstTask, new JsonSerializerOptions { WriteIndented = false })}");

					// Log all properties
					foreach (var prop in firstTask.EnumerateObject())
					{
						_logger.LogInformation($"   Property: {prop.Name} = {prop.Value}");
					}
				}

				// 🔥 Fetch users (GIỐNG TASKSCONTROLLER)
				var users = await GetUsersFromLocalApi();

				// Calculate KPIs
				var dashboardData = CalculateDashboardKPIs(tasks, users);

				_logger.LogInformation($"📊 [DASHBOARD] KPI Summary:");
				_logger.LogInformation($"   - Total Tasks: {dashboardData.TotalTasks}");
				_logger.LogInformation($"   - Pending: {dashboardData.PendingTasks}");
				_logger.LogInformation($"   - In Progress: {dashboardData.InProgressTasks}");
				_logger.LogInformation($"   - Completed: {dashboardData.CompletedTasks} ({dashboardData.CompletionRate}%)");
				_logger.LogInformation($"   - Overdue: {dashboardData.OverdueTasks}");
				_logger.LogInformation($"   - Average Progress: {dashboardData.AverageProgress}%");
				_logger.LogInformation($"   - Team Members: {dashboardData.TasksByAssignee.Count}");

				return View(dashboardData);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DASHBOARD] Fatal error: {ex.Message}");
				_logger.LogError($"❌ [DASHBOARD] Exception type: {ex.GetType().Name}");
				_logger.LogError($"❌ [DASHBOARD] StackTrace: {ex.StackTrace}");

				if (ex.InnerException != null)
				{
					_logger.LogError($"❌ [DASHBOARD] Inner exception: {ex.InnerException.Message}");
				}

				ViewBag.Error = "Không thể tải dữ liệu dashboard.";
				return View(new DashboardViewModel());
			}
		}

		private DashboardViewModel CalculateDashboardKPIs(JsonElement tasks, JsonElement users)
		{
			var model = new DashboardViewModel();

			if (tasks.ValueKind != JsonValueKind.Array)
			{
				_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Tasks is not an array, ValueKind: {tasks.ValueKind}");
				return model;
			}

			// Total tasks
			model.TotalTasks = tasks.GetArrayLength();

			if (model.TotalTasks == 0)
			{
				_logger.LogInformation("ℹ️ [DASHBOARD-KPI] No tasks found");
				return model;
			}

			_logger.LogInformation($"📊 [DASHBOARD-KPI] Processing {model.TotalTasks} tasks...");

			// Create user mapping for quick lookup
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

						if (!userMap.ContainsKey(id))
						{
							userMap[id] = name ?? $"User #{id}";
						}
					}
				}
				_logger.LogInformation($"📋 [DASHBOARD-KPI] Mapped {userMap.Count} users");
			}

			// Process each task
			var taskCounter = 0;
			foreach (var task in tasks.EnumerateArray())
			{
				taskCounter++;
				var taskId = task.TryGetProperty("task_id", out var tid) ? tid.GetInt32() : 0;

				// 🔥 Get task status - CASE INSENSITIVE với nhiều variations
				var status = "";
				if (task.TryGetProperty("status", out var statusProp))
				{
					var rawStatus = statusProp.GetString();
					status = rawStatus?.ToLower()?.Trim().Replace(" ", "").Replace("_", "") ?? "";

					// Debug first 5 tasks
					if (taskCounter <= 5)
					{
						_logger.LogInformation($"🔍 [DASHBOARD-KPI] Task #{taskCounter}: ID={taskId}, Status raw='{rawStatus}', normalized='{status}'");
					}
				}
				else
				{
					_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Task {taskId} has no 'status' property");
				}

				// 🔥 Count by status - NORMALIZED (no spaces, no underscores)
				if (status == "todo" || status == "pending" || status == "notstarted")
				{
					model.PendingTasks++;
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
					// Log unknown status for debugging
					_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Unknown status: '{status}' (raw: {task.GetProperty("status").GetString()}) for task {taskId}");
				}

				// Calculate total progress for average
				if (task.TryGetProperty("progress_percentage", out var progress))
				{
					try
					{
						var progressValue = progress.ValueKind == JsonValueKind.Number
							? progress.GetInt32()
							: 0;
						model.TotalProgress += progressValue;
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Error parsing progress for task {taskId}: {ex.Message}");
					}
				}

				// Check if task is overdue
				var isOverdue = false;
				var isCompletedStatus = status == "completed" || status == "done" || status == "complete" || status == "finished";

				if (task.TryGetProperty("deadline", out var deadlineProp) && !isCompletedStatus)
				{
					try
					{
						var deadlineStr = deadlineProp.GetString();
						if (!string.IsNullOrEmpty(deadlineStr) && DateTime.TryParse(deadlineStr, out var deadline))
						{
							if (deadline < DateTime.Now)
							{
								isOverdue = true;
								model.OverdueTasks++;
							}
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Error parsing deadline for task {taskId}: {ex.Message}");
					}
				}

				// Track tasks by assignee (người được giao)
				if (task.TryGetProperty("assignee_id", out var assigneeIdProp))
				{
					try
					{
						var assigneeId = assigneeIdProp.GetInt32();

						if (assigneeId > 0)
						{
							// Initialize assignee stats if not exists
							if (!model.TasksByAssignee.ContainsKey(assigneeId))
							{
								var assigneeName = userMap.ContainsKey(assigneeId)
									? userMap[assigneeId]
									: $"User #{assigneeId}";

								model.TasksByAssignee[assigneeId] = new AssigneeTaskStats
								{
									AssigneeId = assigneeId,
									AssigneeName = assigneeName
								};
							}

							var stats = model.TasksByAssignee[assigneeId];
							stats.TotalTasks++;

							// Count by status for each assignee - NORMALIZED
							if (status == "todo" || status == "pending" || status == "notstarted")
							{
								stats.PendingTasks++;
							}
							else if (status == "inprogress" || status == "ongoing" || status == "active")
							{
								stats.InProgressTasks++;
							}
							else if (status == "completed" || status == "done" || status == "complete" || status == "finished")
							{
								stats.CompletedTasks++;
							}

							// Count overdue tasks for assignee
							if (isOverdue)
							{
								stats.OverdueTasks++;
							}

							// Add progress for average calculation
							if (task.TryGetProperty("progress_percentage", out var assigneeProgress))
							{
								stats.TotalProgress += assigneeProgress.ValueKind == JsonValueKind.Number
									? assigneeProgress.GetInt32()
									: 0;
							}
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning($"⚠️ [DASHBOARD-KPI] Error processing assignee for task {taskId}: {ex.Message}");
					}
				}
			}

			_logger.LogInformation($"📊 [DASHBOARD-KPI] Status breakdown: Pending={model.PendingTasks}, InProgress={model.InProgressTasks}, Completed={model.CompletedTasks}, Overdue={model.OverdueTasks}");

			// Calculate average progress
			if (model.TotalTasks > 0)
			{
				model.AverageProgress = model.TotalProgress / model.TotalTasks;
			}

			// Calculate completion rate
			if (model.TotalTasks > 0)
			{
				model.CompletionRate = (int)Math.Round((double)model.CompletedTasks / model.TotalTasks * 100);
			}

			// Calculate average progress for each assignee
			foreach (var assignee in model.TasksByAssignee.Values)
			{
				if (assignee.TotalTasks > 0)
				{
					assignee.AverageProgress = assignee.TotalProgress / assignee.TotalTasks;
				}
			}

			// Recent tasks (last 5) ordered by created_at
			model.RecentTasks = tasks.EnumerateArray()
				.OrderByDescending(t =>
				{
					if (t.TryGetProperty("created_at", out var created))
					{
						try
						{
							var createdStr = created.GetString();
							if (!string.IsNullOrEmpty(createdStr) && DateTime.TryParse(createdStr, out var date))
							{
								return date;
							}
						}
						catch { }
					}
					return DateTime.MinValue;
				})
				.Take(5)
				.ToList();

			// Top performers (minimum 1 task, sorted by completion rate then total completed)
			model.TopPerformers = model.TasksByAssignee.Values
				.Where(a => a.TotalTasks >= 1) // At least 1 task
				.OrderByDescending(a => a.CompletionRate)
				.ThenByDescending(a => a.CompletedTasks)
				.ThenByDescending(a => a.TotalTasks)
				.Take(3)
				.ToList();

			_logger.LogInformation($"🏆 [DASHBOARD-KPI] Top Performers:");
			foreach (var performer in model.TopPerformers)
			{
				_logger.LogInformation($"   - {performer.AssigneeName}: {performer.CompletedTasks}/{performer.TotalTasks} ({performer.CompletionRate}%)");
			}

			return model;
		}
	}

	// ViewModel for Dashboard
	public class DashboardViewModel
	{
		public int TotalTasks { get; set; }
		public int PendingTasks { get; set; }
		public int InProgressTasks { get; set; }
		public int CompletedTasks { get; set; }
		public int OverdueTasks { get; set; }
		public int AverageProgress { get; set; }
		public int CompletionRate { get; set; }
		public int TotalProgress { get; set; }
		public Dictionary<int, AssigneeTaskStats> TasksByAssignee { get; set; } = new();
		public List<JsonElement> RecentTasks { get; set; } = new();
		public List<AssigneeTaskStats> TopPerformers { get; set; } = new();
	}

	public class AssigneeTaskStats
	{
		public int AssigneeId { get; set; }
		public string AssigneeName { get; set; } = "Unknown";
		public int TotalTasks { get; set; }
		public int PendingTasks { get; set; }
		public int InProgressTasks { get; set; }
		public int CompletedTasks { get; set; }
		public int OverdueTasks { get; set; }
		public int TotalProgress { get; set; }
		public int AverageProgress { get; set; }

		// Completion rate based on completed vs total tasks
		public int CompletionRate => TotalTasks > 0
			? (int)Math.Round((double)CompletedTasks / TotalTasks * 100)
			: 0;
	}
}