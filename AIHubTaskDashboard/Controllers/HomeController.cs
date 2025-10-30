using Microsoft.AspNetCore.Mvc;
using AIHubTaskDashboard.ViewModel;
using AIHubTaskDashboard.Services;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApiClientService _api;

        public HomeController(ApiClientService api)
        {
            _api = api;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var res = await _api.GetAsync("api/v1/Members");
                if (string.IsNullOrWhiteSpace(res))
                    return View(new List<MemberViewModel>());

                var json = JsonDocument.Parse(res).RootElement;

                var members = new List<MemberViewModel>();

                foreach (var item in json.EnumerateArray())
                {
                    members.Add(new MemberViewModel
                    {
                        FullName = item.GetProperty("name").GetString()!,
                        Role = item.GetProperty("role").GetString()!,
                        Position = item.GetProperty("position").GetString()!,
                        AvatarUrl = item.TryGetProperty("avatar_url", out var avatar) && !string.IsNullOrEmpty(avatar.GetString())
            ? avatar.GetString()!
            : "/images/default-avatar.png",

                        StatusColor = item.TryGetProperty("status", out var status)
            ? status.GetString()!.ToLower()
            : "online",

                    });
                }

                return View(members);
            }
            catch
            {
                return View(new List<MemberViewModel>());
            }
        }

    }
}
