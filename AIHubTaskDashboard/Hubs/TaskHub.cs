using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace AIHubTaskDashboard.Hubs
{
    public class TaskHub : Hub
    {
        // Khi có client kết nối
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("ReceiveMessage", "System", "Connected to TaskHub ✅");
            await base.OnConnectedAsync();
        }

        // Gửi thông báo cho tất cả client
        public async Task BroadcastTaskUpdate(string message)
        {
            await Clients.All.SendAsync("ReceiveTaskUpdate", message);
        }

        // Gửi thông báo chỉ đến người nhận cụ thể
        public async Task SendToUser(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveTaskUpdate", message);
        }
    }
}
