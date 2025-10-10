using AIHubTaskDashboard.Models;
using System.Net.Http.Json;

namespace AIHubTaskDashboard.Services
{
    public class TaskApiService
    {
        private readonly HttpClient _http;

        public TaskApiService(HttpClient http)
        {
            _http = http;
            _http.BaseAddress = new Uri("https://aihubtasktracker-bwbz.onrender.com/");
        }

        public async Task<List<TaskModel>> GetTasksAsync()
        {

            var res = await _http.GetFromJsonAsync<List<TaskModel>>("Tasks");
            return res ?? new List<TaskModel>();
        }

        public async Task CreateTaskAsync(TaskModel task)
        {
            var res = await _http.PostAsJsonAsync("Tasks", task);
            res.EnsureSuccessStatusCode();
        }

        public async Task DeleteTaskAsync(int id)
        {

            var res = await _http.DeleteAsync($"Tasks/{id}");
            res.EnsureSuccessStatusCode();
        }
    }
}
