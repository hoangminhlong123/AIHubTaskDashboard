using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
﻿using Microsoft.AspNetCore.Mvc;
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

				_logger.LogInformation($"✅ [DASHBOARD] Loaded {tasks.GetArrayLength()} tasks");

				// Fetch users
				JsonElement users;
				try
				{
					using var httpClient = new HttpClient();
					var request = HttpContext.Request;
					var baseUrl = $"{request.Scheme}://{request.Host}/";
					httpClient.BaseAddress = new Uri(baseUrl);
					httpClient.Timeout = TimeSpan.FromSeconds(15);

					var response = await httpClient.GetAsync("api/v1/users");
					var usersRes = await response.Content.ReadAsStringAsync();

					if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(usersRes))
					{
						users = JsonDocument.Parse(usersRes).RootElement;
						_logger.LogInformation($"✅ [DASHBOARD] Loaded {users.GetArrayLength()} users");
					}
					else
					{
						users = JsonDocument.Parse("[]").RootElement;
					}
				}
				catch (Exception ex)
				{
					_logger.LogError($"❌ [DASHBOARD] Error loading users: {ex.Message}");
					users = JsonDocument.Parse("[]").RootElement;
				}

				// Calculate KPIs
				var dashboardData = CalculateDashboardKPIs(tasks, users);

				_logger.LogInformation($"📊 [DASHBOARD] KPI Summary:");
				_logger.LogInformation($"   - Total Tasks: {dashboardData.TotalTasks}");
				_logger.LogInformation($"   - Completed: {dashboardData.CompletedTasks} ({dashboardData.CompletionRate}%)");
				_logger.LogInformation($"   - In Progress: {dashboardData.InProgressTasks}");
				_logger.LogInformation($"   - Overdue: {dashboardData.OverdueTasks}");
				_logger.LogInformation($"   - Average Progress: {dashboardData.AverageProgress}%");
				_logger.LogInformation($"   - Team Members: {dashboardData.TasksByAssignee.Count}");

				return View(dashboardData);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [DASHBOARD] Fatal error: {ex.Message}");
				_logger.LogError($"❌ [DASHBOARD] StackTrace: {ex.StackTrace}");
				ViewBag.Error = "Không thể tải dữ liệu dashboard.";
				return View(new DashboardViewModel());
			}
		}

		private DashboardViewModel CalculateDashboardKPIs(JsonElement tasks, JsonElement users)
		{
			var model = new DashboardViewModel();

			if (tasks.ValueKind != JsonValueKind.Array)
			{
				_logger.LogWarning("⚠️ [DASHBOARD] Tasks is not an array");
				return model;
			}

			// Total tasks
			model.TotalTasks = tasks.GetArrayLength();

			if (model.TotalTasks == 0)
			{
				_logger.LogInformation("ℹ️ [DASHBOARD] No tasks found");
				return model;
			}

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
				_logger.LogInformation($"📋 [DASHBOARD] Mapped {userMap.Count} users");
			}

			// Process each task
			foreach (var task in tasks.EnumerateArray())
			{
				var taskId = task.TryGetProperty("task_id", out var tid) ? tid.GetInt32() : 0;

				// Get task status
				var status = "";
				if (task.TryGetProperty("status", out var statusProp))
				{
					status = statusProp.GetString()?.ToLower() ?? "";
				}

				// Count by status
				switch (status)
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

				// Calculate total progress for average
				if (task.TryGetProperty("progress_percentage", out var progress))
				{
					model.TotalProgress += progress.GetInt32();
				}

				// Check if task is overdue
				var isOverdue = false;
				if (task.TryGetProperty("deadline", out var deadlineProp) && status != "completed" && status != "done")
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
						_logger.LogWarning($"⚠️ [DASHBOARD] Error parsing deadline for task {taskId}: {ex.Message}");
					}
				}

				// Track tasks by assignee (người được giao)
				if (task.TryGetProperty("assignee_id", out var assigneeIdProp))
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

						// Count by status for each assignee
						switch (status)
						{
							case "to do":
							case "pending":
								stats.PendingTasks++;
								break;
							case "in progress":
								stats.InProgressTasks++;
								break;
							case "completed":
							case "done":
								stats.CompletedTasks++;
								break;
						}

						// Count overdue tasks for assignee
						if (isOverdue)
						{
							stats.OverdueTasks++;
						}

						// Add progress for average calculation
						if (task.TryGetProperty("progress_percentage", out var assigneeProgress))
						{
							stats.TotalProgress += assigneeProgress.GetInt32();
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

			_logger.LogInformation($"🏆 [DASHBOARD] Top Performers:");
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
