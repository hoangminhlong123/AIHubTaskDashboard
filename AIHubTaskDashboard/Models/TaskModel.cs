namespace AIHubTaskDashboard.Models
{
    public class TaskModel
    {
        public int Id { get; set; }
        public string MemberName { get; set; }
        public string TaskTitle { get; set; }
        public string Status { get; set; }
        public DateTime Deadline { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
