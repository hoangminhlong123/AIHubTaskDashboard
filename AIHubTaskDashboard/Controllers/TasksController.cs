using AIHubTaskDashboard.Models;
using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIHubTaskDashboard.Controllers
{
    public class TasksController : Controller
    {
        private readonly TaskApiService _api;

        public TasksController(TaskApiService api)
        {
            _api = api;
        }

        public async Task<IActionResult> Index()
        {
            var tasks = await _api.GetTasksAsync();
            return View(tasks);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskModel model)
        {
            if (ModelState.IsValid)
                await _api.CreateTaskAsync(model);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _api.DeleteTaskAsync(id);
            return RedirectToAction("Index");
        }

    }
}
