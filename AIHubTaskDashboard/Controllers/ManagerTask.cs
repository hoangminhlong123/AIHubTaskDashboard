using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
    public class ManagerTask : Controller
    {
        // Mock data backend
        private static List<JsonElement> _mockTasks = new List<JsonElement>();
        private static List<JsonElement> _mockUsers = new List<JsonElement>
        {
            JsonDocument.Parse(@"{""id"":1,""name"":""Thiện""}").RootElement,
            JsonDocument.Parse(@"{""id"":2,""name"":""Long""}").RootElement,
            JsonDocument.Parse(@"{""id"":3,""name"":""Dinh""}").RootElement
        };

        static ManagerTask()
        {
            // Khởi tạo vài task mock
            _mockTasks.Add(JsonDocument.Parse(@"{""id"":1,""title"":""Fix UI bug"",""description"":""Layout dashboard"",""status"":""Todo"",""assignee_id"":2}").RootElement);
            _mockTasks.Add(JsonDocument.Parse(@"{""id"":2,""title"":""Add SignalR"",""description"":""Realtime updates"",""status"":""InProgress"",""assignee_id"":3}").RootElement);
        }

        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.Users = _mockUsers;
            return View(_mockTasks);
        }

        [HttpPost]
        public IActionResult Create(string title, string description, string status, int assignee_id)
        {
            int newId = _mockTasks.Any() ? _mockTasks.Max(t => t.GetProperty("id").GetInt32()) + 1 : 1;
            var taskJson = JsonDocument.Parse($@"
                {{
                    ""id"":{newId},
                    ""title"":""{title}"",
                    ""description"":""{description}"",
                    ""status"":""{status}"",
                    ""assignee_id"":{assignee_id}
                }}").RootElement;

            _mockTasks.Add(taskJson);
            return Json(new { success = true, task = taskJson });
        }

        [HttpPost]
        public IActionResult Update(int id, string title, string description, string status, int assignee_id)
        {
            var index = _mockTasks.FindIndex(t => t.GetProperty("id").GetInt32() == id);
            if (index == -1)
                return Json(new { success = false, message = "Task not found" });

            var taskJson = JsonDocument.Parse($@"
                {{
                    ""id"":{id},
                    ""title"":""{title}"",
                    ""description"":""{description}"",
                    ""status"":""{status}"",
                    ""assignee_id"":{assignee_id}
                }}").RootElement;

            _mockTasks[index] = taskJson;
            return Json(new { success = true, task = taskJson });
        }
    }
}
