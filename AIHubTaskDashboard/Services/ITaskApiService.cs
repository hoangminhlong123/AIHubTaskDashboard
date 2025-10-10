using AIHubTaskDashboard.Models;

namespace AIHubTaskDashboard.Services
{
    public interface ITaskApiService
    {
        Task<List<TaskModel>> GetTasksAsync();
        Task CreateTaskAsync(TaskModel model);
        Task DeleteTaskAsync(int id);

    }
}
