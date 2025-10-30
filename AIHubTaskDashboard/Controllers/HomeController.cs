using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
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

				// Fetch all tasks
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
					{
						tasks = JsonDocument.Parse($"[{tasksRes}]").RootElement;
					}
				}

				// Fetch users
				JsonElement users;
				try
				{
					var usersRes = await _api.GetAsync("api/v1/users");
					users = JsonDocument.Parse(usersRes).RootElement;
				}
				catch
				{
					users = JsonDocument.Parse("[]").RootElement;
				}

				// Calculate KPIs
				var dashboardData = CalculateDashboardKPIs(tasks, users);

				return View(dashboardData);
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error loading dashboard: {ex.Message}");
				ViewBag.Error = "Không thể tải dữ liệu dashboard.";
				return View(new DashboardViewModel());
			}
		}

		private DashboardViewModel CalculateDashboardKPIs(JsonElement tasks, JsonElement users)
		{
			var model = new DashboardViewModel();

			if (tasks.ValueKind != JsonValueKind.Array)
			{
				return model;
			}

			// Total tasks
			model.TotalTasks = tasks.GetArrayLength();

			if (model.TotalTasks == 0)
			{
				return model;
			}

			// Status breakdown & calculations
			foreach (var task in tasks.EnumerateArray())
			{
				// Status counting
				if (task.TryGetProperty("status", out var status))
				{
					var statusValue = status.GetString()?.ToLower() ?? "";

					switch (statusValue)
					{
						case "to do":
						case "pending":
							model.PendingTasks++;
							break;
						case "in progress":
							model.InProgressTasks++;
							break;
						case "completed":
						case "done":
							model.CompletedTasks++;
							break;
					}
				}

				// Calculate average progress
				if (task.TryGetProperty("progress_percentage", out var progress))
				{
					model.TotalProgress += progress.GetInt32();
				}

				// Overdue tasks
				if (task.TryGetProperty("deadline", out var deadline) &&
					task.TryGetProperty("status", out var taskStatus))
				{
					try
					{
						var deadlineDate = deadline.GetDateTime();
						var statusValue = taskStatus.GetString()?.ToLower();

						if (deadlineDate < DateTime.UtcNow &&
							statusValue != "completed" &&
							statusValue != "done")
						{
							model.OverdueTasks++;
						}
					}
					catch { }
				}

				// Tasks by assignee
				if (task.TryGetProperty("assignee_id", out var assigneeId))
				{
					var id = assigneeId.GetInt32();
					if (id > 0)
					{
						if (!model.TasksByAssignee.ContainsKey(id))
						{
							model.TasksByAssignee[id] = new AssigneeTaskStats { AssigneeId = id };
						}
						model.TasksByAssignee[id].TotalTasks++;

						var taskStatusValue = task.TryGetProperty("status", out var st)
							? st.GetString()?.ToLower()
							: "";

						if (taskStatusValue == "completed" || taskStatusValue == "done")
						{
							model.TasksByAssignee[id].CompletedTasks++;
						}
					}
				}
			}

			// Calculate average progress
			if (model.TotalTasks > 0)
			{
				model.AverageProgress = model.TotalProgress / model.TotalTasks;
			}

			// Calculate completion rate
			if (model.TotalTasks > 0)
			{
				model.CompletionRate = (int)((double)model.CompletedTasks / model.TotalTasks * 100);
			}

			// Map assignee names from users
			if (users.ValueKind == JsonValueKind.Array)
			{
				foreach (var user in users.EnumerateArray())
				{
					if (user.TryGetProperty("user_id", out var userId))
					{
						var id = userId.GetInt32();
						if (model.TasksByAssignee.ContainsKey(id) &&
							user.TryGetProperty("full_name", out var fullName))
						{
							model.TasksByAssignee[id].AssigneeName = fullName.GetString() ?? "Unknown";
						}
					}
				}
			}

			// Recent tasks (last 5) with full task data
			model.RecentTasks = tasks.EnumerateArray()
				.OrderByDescending(t =>
				{
					if (t.TryGetProperty("created_at", out var created))
					{
						try { return created.GetDateTime(); }
						catch { return DateTime.MinValue; }
					}
					return DateTime.MinValue;
				})
				.Take(5)
				.ToList();

			// Top performers (users with highest completion rate, min 2 tasks)
			model.TopPerformers = model.TasksByAssignee.Values
				.Where(a => a.TotalTasks >= 2)
				.OrderByDescending(a => a.CompletionRate)
				.ThenByDescending(a => a.CompletedTasks)
				.Take(3)
				.ToList();

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
		public int CompletedTasks { get; set; }
		public int CompletionRate => TotalTasks > 0 ? (int)((double)CompletedTasks / TotalTasks * 100) : 0;
	}
}