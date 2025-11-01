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

        public async Task<IActionResult> Index(string? status, int? assignee_id, int page = 1, int pageSize = 10)
        {
            try
            {
                string endpoint = "api/v1/tasks";
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

                var taskList = tasks.EnumerateArray().ToList();
                if (!string.IsNullOrEmpty(status))
                    taskList = taskList.Where(t => t.TryGetProperty("status", out var s) && s.GetString() == status).ToList();

                if (assignee_id.HasValue && assignee_id.Value != 0)
                    taskList = taskList.Where(t => t.TryGetProperty("assignee_id", out var a) && a.GetInt32() == assignee_id.Value).ToList();

                int totalTasks = taskList.Count;
                int totalPages = (int)Math.Ceiling(totalTasks / (double)pageSize);

                var pagedTasks = taskList.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                var usersRes = await _api.GetAsync("api/v1/members");
                var usersJson = JsonDocument.Parse(string.IsNullOrWhiteSpace(usersRes) ? "[]" : usersRes).RootElement;
                ViewBag.Users = usersJson;

                var pagedJson = JsonDocument.Parse(JsonSerializer.Serialize(pagedTasks)).RootElement;
                return View(pagedJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tasks");
                var emptyJson = JsonDocument.Parse("[]").RootElement;
                return View(emptyJson);
            }
        }



        [HttpGet]
		public async Task<IActionResult> Create()
		{
			JsonElement users;

			try
			{
				var usersRes = await _api.GetAsync("api/v1/members");
				users = JsonDocument.Parse(usersRes).RootElement;
			}
			catch
			{
				users = JsonDocument.Parse("[]").RootElement;
			}

			ViewBag.Users = users;
			var emptyJson = JsonDocument.Parse("{}").RootElement;
			return View(emptyJson);
		}

		[HttpPost]
		public async Task<IActionResult> Create(string title, string description, string status, int progress_percentage, int assignee_id)
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
				_logger.LogInformation($"➕ Creating task: {title}");

				// 1️⃣ Tạo task trong ClickUp TRƯỚC
				var clickupTaskId = await _clickUp.CreateTaskAsync(title, description, status);

				if (clickupTaskId == null)
				{
					_logger.LogWarning("⚠️ Failed to create task in ClickUp, creating in Dashboard only");
				}

				// 2️⃣ Tạo task trong Dashboard với clickup_id
				var collaborators = new List<int> { assignee_id };
				string expected_output = "Chưa có yêu cầu đầu ra cụ thể.";
				string deadline = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

				var payload = new
				{
					clickup_id = clickupTaskId, // ✅ Link với ClickUp
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

				await _api.PostAsync("api/v1/tasks", payload);

				_logger.LogInformation($"✅ Task created: Dashboard + ClickUp ({clickupTaskId})");
				TempData["Success"] = "Task đã được tạo thành công!";
				return RedirectToAction("Index", "Tasks");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ Create error: {ex.Message}");
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

            // Lấy danh sách users cho dropdown
            var usersRes = await _api.GetAsync("api/v1/members");
            var usersJson = string.IsNullOrWhiteSpace(usersRes) ? JsonDocument.Parse("[]").RootElement : JsonDocument.Parse(usersRes).RootElement;
            ViewBag.Users = usersJson;

            return View(task);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(
            int id,
            string title,
            string description,
            string status,
            int progress_percentage,
            int assignee_id
        )
        {
            try
            {
                _logger.LogInformation($"🔄 Updating task: {id}");

                // 1️⃣ Lấy task từ Dashboard để có clickup_id
                var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");
                var task = JsonDocument.Parse(taskRes).RootElement;

                // 2️⃣ Update ClickUp nếu có clickup_id
                if (task.TryGetProperty("clickup_id", out var clickupIdProp))
                {
                    var clickupId = clickupIdProp.GetString();
                    if (!string.IsNullOrEmpty(clickupId))
                    {
                        _logger.LogInformation($"🔄 Syncing update to ClickUp: {clickupId}");
                        await _clickUp.UpdateTaskAsync(clickupId, title, description, status);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Task has no clickup_id, skipping ClickUp sync");
                    }
                }

                // 3️⃣ Update Dashboard
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
                _logger.LogInformation($"Deleting task {id}");

                var taskRes = await _api.GetAsync($"api/v1/tasks/{id}");
                var task = JsonDocument.Parse(taskRes).RootElement;

                if (task.TryGetProperty("clickup_id", out var clickupIdProp))
                {
                    var clickupId = clickupIdProp.GetString();
                    if (!string.IsNullOrEmpty(clickupId))
                        await _clickUp.DeleteTaskAsync(clickupId);
                }

                await _api.DeleteAsync($"api/v1/tasks/{id}");

                TempData["Success"] = "Task đã được xóa!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Delete error task {id}");
                TempData["Error"] = $"Xóa Task thất bại: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

    }
}