using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
    public class TasksController : Controller
    {
        private readonly ApiClientService _api;

        public TasksController(ApiClientService api)
        {
            _api = api;
        }

        public async Task<IActionResult> Index(string? status, int? assignee_id)
        {
            try
            {
                string endpoint = "api/v1/tasks";
                var query = new List<string>();

                // Chỉ lọc theo assignee_id nếu người dùng chọn filter
                if (assignee_id.HasValue && assignee_id.Value != 0)
                {
                    query.Add($"assignee_id={assignee_id.Value}");
                }

                // Lọc theo status nếu có
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

                // Lấy danh sách Users cho view
                try
                {
                    var usersRes = await _api.GetAsync("api/v1/users");
                    ViewBag.Users = JsonDocument.Parse(usersRes).RootElement;
                }
                catch
                {
                    ViewBag.Users = JsonDocument.Parse("[]").RootElement;
                }

                return View(tasks);
            }
            catch (Exception ex)
            {
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
            var collaborators = new List<int> { assignee_id };
            string expected_output = "Chưa có yêu cầu đầu ra cụ thể.";
            string deadline = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string notion_link = string.Empty; 
            var payload = new
            {
                title,
                description,
                assigner_id, 
                assignee_id,
                collaborators,
                expected_output,
                deadline,
                status,
                progress_percentage,
                notion_link
            };

            if (assigner_id == 0)
            {
                TempData["Error"] = "Người giao không hợp lệ. Vui lòng đăng nhập lại (ID người dùng không được lưu trong Session).";
                return RedirectToAction("Create");
            }
            try
            {
                await _api.PostAsync("api/v1/tasks", payload);
                return RedirectToAction("Index", "Tasks");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Tạo Task thất bại. Vui lòng kiểm tra Server Logs (Lỗi: {ex.Message}).";
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

            return View(task);
        }

        // POST: /Tasks/Edit/5
        [HttpPost]
        public async Task<IActionResult> Edit(int id, string title, string description, string status, int progress_percentage)
        {
            var payload = new { title, description, status, progress_percentage };
            await _api.PutAsync($"api/v1/tasks/{id}", payload);
            return RedirectToAction("Index");
        }

        // POST: /Tasks/Delete/5
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            await _api.DeleteAsync($"api/v1/tasks/{id}");
            return RedirectToAction("Index");
        }


    }
}
