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
            // Lấy ID người dùng hiện tại từ Session (Giả định đã đăng nhập thành công)
            string currentUserIdString = HttpContext.Session.GetString("id");
            int currentUserId = 0;
            if (!string.IsNullOrEmpty(currentUserIdString))
            {
                int.TryParse(currentUserIdString, out currentUserId);
            }

            // Kiểm tra đăng nhập (Nếu ID là 0, chuyển hướng về Login)
            if (currentUserId == 0)
            {
                // Giả định bạn có AccountController
                return RedirectToAction("Login", "Account");
            }

            try
            {
                string endpoint = "api/v1/tasks";
                var query = new List<string>();

                // SỬA LỖI: Chỉ lọc theo assignee_id một lần
                bool isAssigneeFilterSet = false;

                if (assignee_id.HasValue && assignee_id.Value != 0)
                {
                    // Lọc theo assignee_id nếu người dùng tự chọn filter hợp lệ
                    query.Add($"assignee_id={assignee_id.Value}");
                    isAssigneeFilterSet = true;
                }
                else if (currentUserId != 0 && !isAssigneeFilterSet)
                {
                    // MẶC ĐỊNH: Lọc theo Task được giao cho người dùng hiện tại
                    query.Add($"assignee_id={currentUserId}");
                    isAssigneeFilterSet = true;
                }

                // Lọc theo Status (chỉ thêm nếu có giá trị)
                if (!string.IsNullOrEmpty(status))
                {
                    query.Add($"status={status}");
                }

                // Tạo Endpoint hoàn chỉnh
                if (query.Count > 0) endpoint += "?" + string.Join("&", query);

                var res = await _api.GetAsync(endpoint);

                JsonElement tasks;
                if (string.IsNullOrWhiteSpace(res))
                {
                    tasks = JsonDocument.Parse("[]").RootElement;
                }
                else
                {
                    tasks = JsonDocument.Parse(res).RootElement;
                    // Xử lý trường hợp API trả về object đơn lẻ thay vì array (ít xảy ra nhưng an toàn)
                    if (tasks.ValueKind != JsonValueKind.Array)
                        tasks = JsonDocument.Parse($"[{res}]").RootElement;
                }

                // Thêm Users vào ViewBag để View có dữ liệu người dùng động
                try
                {
                    var usersRes = await _api.GetAsync("api/v1/users");
                    ViewBag.Users = JsonDocument.Parse(usersRes).RootElement;
                }
                catch
                {
                    // Đảm bảo View không bị lỗi nếu không tải được Users
                    ViewBag.Users = JsonDocument.Parse("[]").RootElement;
                }

                return View(tasks);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi vào hệ thống (nếu có ILogger)
                // logger.LogError(ex, "Lỗi khi tải danh sách Task.");

                var emptyJson = JsonDocument.Parse("[]").RootElement;
                return View(emptyJson);
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
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
