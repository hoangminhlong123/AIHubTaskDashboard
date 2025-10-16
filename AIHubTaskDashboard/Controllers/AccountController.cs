using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApiClientService _api;

        public AccountController(ApiClientService api)
        {
            _api = api;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            try
            {
                var payload = new { email, password };
                var res = await _api.PostAsync("api/v1/auth/login", payload);
                // ... (Console.WriteLine) ...

                if (string.IsNullOrWhiteSpace(res))
                {
                    ViewBag.Error = "Không nhận được phản hồi từ máy chủ.";
                    return View();
                }

                var json = JsonDocument.Parse(res).RootElement;

                if (json.TryGetProperty("token", out var token))
                {
                    var tokenStr = token.GetString()!;
                    HttpContext.Session.SetString("AuthToken", tokenStr);

                    // Gọi API Profile
                    var profileRes = await _api.GetAsync("api/v1/users/profile");
                    var profileJson = JsonDocument.Parse(profileRes).RootElement;

                    // BƯỚC 1: LƯU USER ID VÀO SESSION VỚI KEY "id"
                    // Key "user_id" là tên field trong model Member của Backend
                    if (profileJson.TryGetProperty("user_id", out var userIdValue))
                    {
                        HttpContext.Session.SetString("id", userIdValue.GetInt32().ToString());
                    }

                    // BƯỚC 2: LƯU FULL NAME VÀO SESSION
                    if (profileJson.TryGetProperty("full_name", out var fullName))
                        HttpContext.Session.SetString("FullName", fullName.GetString()!);

                    return RedirectToAction("Index", "Home");
                }


                ViewBag.Error = json.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : "Đăng nhập thất bại. Vui lòng kiểm tra lại.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Lỗi: {ex.Message}";
            }

            return View();
        }


        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        public async Task<IActionResult> Register(string full_name, string email, string password)
        {
            try
            {
                var payload = new { full_name, email, password };
                var res = await _api.PostAsync("api/v1/auth/register", payload);

                if (string.IsNullOrWhiteSpace(res))
                {
                    ViewBag.Error = "Không nhận được phản hồi từ máy chủ.";
                    return View();
                }

                var json = JsonDocument.Parse(res).RootElement;

                if (json.TryGetProperty("message", out var msg))
                {
                    TempData["Success"] = msg.GetString();
                    return RedirectToAction("Login");
                }

                ViewBag.Error = json.TryGetProperty("error", out var err)
                    ? err.GetString()
                    : "Đăng ký thất bại.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Lỗi: {ex.Message}";
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            try
            {
                var res = await _api.GetAsync("api/v1/users/profile");

                if (string.IsNullOrWhiteSpace(res))
                {
                    ViewBag.Error = "Không thể tải thông tin hồ sơ.";
                    return View();
                }

                var profile = JsonDocument.Parse(res).RootElement;
                return View(profile);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Lỗi: {ex.Message}";
                return View();
            }
        }
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string full_name, string email, string role, string position)
        {
            try
            {
                var payload = new { full_name, email, role, position };
                var res = await _api.PutAsync("api/v1/users/profile", payload);
                var json = JsonDocument.Parse(res).RootElement;

                if (json.TryGetProperty("message", out var msg))
                {
                    HttpContext.Session.SetString("FullName", full_name);

                    TempData["Success"] = msg.GetString();
                    return RedirectToAction("Profile");
                }

                TempData["Error"] = json.TryGetProperty("message", out var errorMsg) ? errorMsg.GetString() : "Cập nhật thất bại";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction("Profile");
        }
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

    }
}
