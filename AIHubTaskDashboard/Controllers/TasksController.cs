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

		public TasksController(
			ApiClientService api,
			ClickUpApiService clickUp,
			ILogger<TasksController> logger)
		{
			_api = api;
			_clickUp = clickUp;
			_logger = logger;
		}

		// ✅ HELPER: Lấy users từ LOCAL UsersController (không qua backend Python)
		private async Task<JsonElement> GetUsersFromLocalApi()
		{
			try
			{
				_logger.LogInformation("🔄 [USERS] Fetching from LOCAL UsersController...");

				using var httpClient = new HttpClient();

				// Lấy base URL động từ current request
				var request = HttpContext.Request;
				var baseUrl = $"{request.Scheme}://{request.Host}/";

				httpClient.BaseAddress = new Uri(baseUrl);
				httpClient.Timeout = TimeSpan.FromSeconds(15);

				_logger.LogInformation($"📍 [USERS] Base URL: {baseUrl}");

				// ✅ GỌI LOCAL ENDPOINT: /api/v1/users (UsersController local)
				var response = await httpClient.GetAsync("api/v1/users");
				var usersRes = await response.Content.ReadAsStringAsync();

				_logger.LogInformation($"📦 [USERS] Response Status: {response.StatusCode}");
				_logger.LogInformation($"📦 [USERS] Response Length: {usersRes?.Length ?? 0}");

				if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(usersRes))
				{
					var users = JsonDocument.Parse(usersRes).RootElement;

					if (users.ValueKind == JsonValueKind.Array)
					{
						_logger.LogInformation($"✅ [USERS] Got {users.GetArrayLength()} users from LOCAL API");
						return users;
					}
					else
					{
						_logger.LogWarning($"⚠️ [USERS] Response is not an array: {users.ValueKind}");
					}
				}
				else
				{
					_logger.LogError($"❌ [USERS] Local API failed: {response.StatusCode}");
					_logger.LogError($"❌ [USERS] Response: {usersRes}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [USERS] Exception: {ex.Message}");
				_logger.LogError($"❌ [USERS] StackTrace: {ex.StackTrace}");
			}

			_logger.LogWarning("⚠️ [USERS] Returning empty array");
			return JsonDocument.Parse("[]").RootElement;
		}

		public async Task<IActionResult> Index(string? status, int? assignee_id)
		{
			try
			{
				string endpoint = "api/v1/tasks";
				var query = new List<string>();

				if (assignee_id.HasValue && assignee_id.Value != 0)
				{
					query.Add($"assignee_id={assignee_id.Value}");
				}

				if (!string.IsNullOrEmpty(status))
				{
					query.Add($"status={status}");
				}

				if (query.Count > 0)
					endpoint += "?" + string.Join("&", query);

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

				// ✅ GỌI LOCAL API thay vì backend Python
				ViewBag.Users = await GetUsersFromLocalApi();

				return View(tasks);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [INDEX] Error: {ex.Message}");
				ViewBag.Users = JsonDocument.Parse("[]").RootElement;
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

				// ✅ GỌI LOCAL API thay vì backend Python
				ViewBag.Users = await GetUsersFromLocalApi();

				var emptyJson = JsonDocument.Parse("{}").RootElement;
				return View(emptyJson);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [CREATE] Error: {ex.Message}");
				ViewBag.Users = JsonDocument.Parse("[]").RootElement;
				var emptyJson = JsonDocument.Parse("{}").RootElement;
				return View(emptyJson);
			}
		}

		[HttpPost]
		public async Task<IActionResult> Create(string title, string description, string status, int progress_percentage, int assignee_id)
		{
			// 🔥 GENERATE UNIQUE REQUEST ID
			var requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
			_logger.LogWarning($"🆔 [CREATE-{requestId}] ===== NEW REQUEST STARTED =====");
			_logger.LogWarning($"🆔 [CREATE-{requestId}] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");

			string userIdString = HttpContext.Session.GetString("id");

			int assigner_id = 0;
			if (!string.IsNullOrEmpty(userIdString))
			{
				int.TryParse(userIdString, out assigner_id);
			}

			if (assigner_id == 0)
			{
				_logger.LogError($"❌ [CREATE-{requestId}] Invalid assigner_id");
				TempData["Error"] = "Người giao không hợp lệ. Vui lòng đăng nhập lại.";
				return RedirectToAction("Create");
			}

			try
			{
				_logger.LogInformation($"➕ [CREATE-{requestId}] Creating task:");
				_logger.LogInformation($"   [CREATE-{requestId}] - Title: {title}");
				_logger.LogInformation($"   [CREATE-{requestId}] - Assignee ID: {assignee_id}");
				_logger.LogInformation($"   [CREATE-{requestId}] - Status: {status}");
				_logger.LogInformation($"   [CREATE-{requestId}] - Description length: {description?.Length ?? 0}");

				// 🔥 STEP 1: Create in ClickUp
				_logger.LogInformation($"🔄 [CREATE-{requestId}] STEP 1: Calling ClickUp API...");
				var clickupTaskId = await _clickUp.CreateTaskAsync(title, description, status, assignee_id);

				if (clickupTaskId == null)
				{
					_logger.LogWarning($"⚠️ [CREATE-{requestId}] STEP 1 FAILED: ClickUp creation failed");
					TempData["Warning"] = "Task được tạo trong Dashboard nhưng không sync được sang ClickUp.";
				}
				else
				{
					_logger.LogInformation($"✅ [CREATE-{requestId}] STEP 1 SUCCESS: ClickUp task ID = {clickupTaskId}");
				}

				// 🔥 STEP 2: Create in Dashboard
				_logger.LogInformation($"🔄 [CREATE-{requestId}] STEP 2: Creating in Dashboard...");

				var collaborators = new List<int> { assignee_id };
				string expected_output = "Chưa có yêu cầu đầu ra cụ thể.";
				string deadline = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

				var payload = new
				{
					clickup_id = clickupTaskId,
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

				_logger.LogInformation($"📤 [CREATE-{requestId}] Sending to Dashboard API...");
				await _api.PostAsync("api/v1/tasks", payload);
				_logger.LogInformation($"✅ [CREATE-{requestId}] STEP 2 SUCCESS: Task created in Dashboard");

				_logger.LogWarning($"🎉 [CREATE-{requestId}] ===== REQUEST COMPLETED SUCCESSFULLY =====");
				_logger.LogWarning($"🎉 [CREATE-{requestId}] Total time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");

				TempData["Success"] = "Task đã được tạo thành công!";
				return RedirectToAction("Index", "Tasks");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [CREATE-{requestId}] EXCEPTION OCCURRED");
				_logger.LogError($"❌ [CREATE-{requestId}] Error: {ex.Message}");
				_logger.LogError($"❌ [CREATE-{requestId}] StackTrace: {ex.StackTrace}");
				TempData["Error"] = $"Tạo Task thất bại: {ex.Message}";
				return RedirectToAction("Create");
			}
		}

		[HttpGet]
		public async Task<IActionResult> Edit(int id)
		{
			var res = await _api.GetAsync($"api/v1/tasks/{id}");
			JsonElement task;

			if (string.IsNullOrWhiteSpace(res))
				task = JsonDocument.Parse("{}").RootElement;
			else
				task = JsonDocument.Parse(res).RootElement;

			// ✅ GỌI LOCAL API thay vì backend Python
			ViewBag.Users = await GetUsersFromLocalApi();

			return View(task);
		}

		[HttpPost]
		public async Task<IActionResult> Edit(int id, string title, string description, string status, int progress_percentage, int? assignee_id)
		{
			try
			{
				_logger.LogInformation($"🔄 Updating task: {id}");

				var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");
				var task = JsonDocument.Parse(taskRes).RootElement;

				if (task.TryGetProperty("clickup_id", out var clickupIdProp))
				{
					var clickupId = clickupIdProp.GetString();
					if (!string.IsNullOrEmpty(clickupId))
					{
						_logger.LogInformation($"🔄 Syncing update to ClickUp: {clickupId}");
						await _clickUp.UpdateTaskAsync(clickupId, title, description, status, assignee_id);
					}
					else
					{
						_logger.LogWarning("⚠️ Task has no clickup_id, skipping ClickUp sync");
					}
				}

				var payload = new
				{
					title,
					description,
					status,
					progress_percentage,
					assignee_id
				};
				await _api.PutAsync($"api/v1/tasks/{id}", payload);

				_logger.LogInformation($"✅ Task updated: {id}");
				TempData["Success"] = "Task đã được cập nhật!";
				return RedirectToAction("Index");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Update error: {ex.Message}");
				TempData["Error"] = $"Cập nhật Task thất bại: {ex.Message}";
				return RedirectToAction("Edit", new { id });
			}
		}

		[HttpPost]
		public async Task<IActionResult> Delete(int id)
		{
			try
			{
				_logger.LogInformation($"🗑️ Deleting task: {id}");

				var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");
				var task = JsonDocument.Parse(taskRes).RootElement;

				if (task.TryGetProperty("clickup_id", out var clickupIdProp))
				{
					var clickupId = clickupIdProp.GetString();
					if (!string.IsNullOrEmpty(clickupId))
					{
						_logger.LogInformation($"🗑️ Deleting from ClickUp: {clickupId}");
						await _clickUp.DeleteTaskAsync(clickupId);
					}
				}

				await _api.DeleteAsync($"api/v1/tasks/{id}");

				_logger.LogInformation($"✅ Task deleted: {id}");
				TempData["Success"] = "Task đã được xóa!";
				return RedirectToAction("Index");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Delete error: {ex.Message}");
				TempData["Error"] = $"Xóa Task thất bại: {ex.Message}";
				return RedirectToAction("Index");
			}
		}
	}
}