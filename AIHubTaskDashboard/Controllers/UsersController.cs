using AIHubTaskDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIHubTaskDashboard.Controllers
{
	[ApiController]
	[Route("api/v1/[controller]")]
	public class UsersController : ControllerBase
	{
		private readonly ApiClientService _api;
		private readonly HttpClient _httpClient;
		private readonly string _clickUpToken;
		private readonly string _teamId;
		private readonly ILogger<UsersController> _logger;

		// Cache users để tránh gọi ClickUp API liên tục
		private static List<object>? _cachedUsers = null;
		private static DateTime _cacheExpiry = DateTime.MinValue;

		public UsersController(
			ApiClientService api,
			IConfiguration config,
			ILogger<UsersController> logger)
		{
			_api = api;
			_logger = logger;

			// Setup ClickUp client
			_httpClient = new HttpClient();
			_clickUpToken = config["ClickUpSettings:Token"] ?? "";
			_teamId = config["ClickUpSettings:TeamId"] ?? "";

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);
			_httpClient.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue(_clickUpToken);
			_httpClient.DefaultRequestHeaders.Add("User-Agent", "AIHubTaskDashboard");
		}

		// 🔥 THÊM ENDPOINT CLEAR CACHE
		[HttpGet("debug/clear-cache")]
		public IActionResult ClearCache()
		{
			_cachedUsers = null;
			_cacheExpiry = DateTime.MinValue;
			_logger.LogInformation("🗑️ [USERS] Cache cleared successfully");
			return Ok(new { message = "Cache cleared successfully", timestamp = DateTime.UtcNow });
		}

		[HttpGet]
		public async Task<IActionResult> GetUsers()
		{
			try
			{
				_logger.LogInformation("📥 [USERS] Fetching all users");

				// 1️⃣ Check cache trước (cache 5 phút)
				if (_cachedUsers != null && DateTime.UtcNow < _cacheExpiry)
				{
					_logger.LogInformation($"✅ [USERS] Returning {_cachedUsers.Count} cached users");
					return Ok(_cachedUsers);
				}

				// 2️⃣ Thử lấy từ backend Python/FastAPI
				try
				{
					var response = await _api.GetAsync("api/v1/users");

					if (!string.IsNullOrEmpty(response))
					{
						var users = JsonDocument.Parse(response).RootElement;

						if (users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0)
						{
							_logger.LogInformation($"✅ [USERS] Got {users.GetArrayLength()} users from backend");

							// Cache lại
							_cachedUsers = new List<object>();
							foreach (var user in users.EnumerateArray())
							{
								_cachedUsers.Add(JsonSerializer.Deserialize<object>(user.GetRawText())!);
							}
							_cacheExpiry = DateTime.UtcNow.AddMinutes(5);

							return Ok(users);
						}
					}
				}
				catch (Exception apiEx)
				{
					_logger.LogWarning($"⚠️ [USERS] Backend API error: {apiEx.Message}");
				}

				// 3️⃣ Backend trống → Lấy từ ClickUp
				_logger.LogInformation("🔄 [USERS] Backend empty, fetching from ClickUp...");

				var clickUpUsers = await GetUsersFromClickUp();

				if (clickUpUsers != null && clickUpUsers.Count > 0)
				{
					_logger.LogInformation($"✅ [USERS] Got {clickUpUsers.Count} users from ClickUp");

					// Cache lại
					_cachedUsers = clickUpUsers;
					_cacheExpiry = DateTime.UtcNow.AddMinutes(5);

					return Ok(clickUpUsers);
				}

				// 4️⃣ Fallback cuối: Trả về current user từ session
				_logger.LogWarning("⚠️ [USERS] No users found anywhere, using session user");

				var sessionUserId = HttpContext.Session.GetString("id");
				var sessionUserName = HttpContext.Session.GetString("username") ?? "Current User";

				var fallbackUsers = new List<object>
				{
					new
					{
						id = int.TryParse(sessionUserId, out var uid) ? uid : 1,
						name = sessionUserName,
						email = $"{sessionUserName.ToLower().Replace(" ", "")}@charm.contact",
						username = sessionUserName,
						clickup_id = "294795597" // Default to Hubos AI
					}
				};

				return Ok(fallbackUsers);
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [USERS] GetUsers error: {ex.Message}");
				return StatusCode(500, new { error = "Failed to fetch users", details = ex.Message });
			}
		}

		[HttpGet("{id}")]
		public async Task<IActionResult> GetUser(int id)
		{
			try
			{
				_logger.LogInformation($"📥 [USERS] Fetching user: {id}");

				// 1️⃣ Thử backend trước
				try
				{
					var response = await _api.GetAsync($"api/v1/users/{id}");

					if (!string.IsNullOrEmpty(response))
					{
						var user = JsonDocument.Parse(response).RootElement;
						_logger.LogInformation($"✅ [USERS] Got user {id} from backend");
						return Ok(user);
					}
				}
				catch (Exception apiEx)
				{
					_logger.LogWarning($"⚠️ [USERS] Backend error: {apiEx.Message}");
				}

				// 2️⃣ Lấy từ cached users hoặc ClickUp
				var users = _cachedUsers ?? await GetUsersFromClickUp();

				if (users != null)
				{
					var userJson = users.FirstOrDefault(u =>
					{
						var json = JsonSerializer.Serialize(u);
						var elem = JsonDocument.Parse(json).RootElement;
						return elem.TryGetProperty("id", out var idProp) && idProp.GetInt32() == id;
					});

					if (userJson != null)
					{
						_logger.LogInformation($"✅ [USERS] Found user {id}");
						return Ok(userJson);
					}
				}

				_logger.LogWarning($"⚠️ [USERS] User {id} not found");
				return NotFound(new { error = "User not found" });
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [USERS] GetUser error: {ex.Message}");

				if (ex.Message.Contains("404"))
					return NotFound(new { error = "User not found" });

				return StatusCode(500, new { error = "Failed to fetch user", details = ex.Message });
			}
		}

		// 🔥 HELPER: Lấy users từ ClickUp Team VÀ AUTO-SYNC VÀO BACKEND
		private async Task<List<object>?> GetUsersFromClickUp()
		{
			try
			{
				if (string.IsNullOrEmpty(_teamId))
				{
					_logger.LogError("❌ [USERS] TeamId not configured in appsettings.json");
					return null;
				}

				_logger.LogInformation($"🌐 [USERS] Fetching ClickUp team members: {_teamId}");

				var response = await _httpClient.GetAsync($"team/{_teamId}");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [USERS] ClickUp API failed: {response.StatusCode} - {content}");
					return null;
				}

				var data = JsonDocument.Parse(content).RootElement;
				var team = data.GetProperty("team");
				var members = team.GetProperty("members");

				var users = new List<object>();

				// 🔥 AUTO-SYNC VÀO BACKEND DATABASE
				foreach (var member in members.EnumerateArray())
				{
					var user = member.GetProperty("user");

					// Extract user info
					string clickUpId = "";
					if (user.TryGetProperty("id", out var idProp))
					{
						clickUpId = idProp.ValueKind == JsonValueKind.Number
							? idProp.GetInt64().ToString()
							: (idProp.GetString() ?? "");
					}

					var username = user.TryGetProperty("username", out var usernameProp)
						? (usernameProp.GetString() ?? "User")
						: "User";

					var email = user.TryGetProperty("email", out var emailProp)
						? (emailProp.GetString() ?? $"{username.ToLower().Replace(" ", "")}@charm.contact")
						: $"{username.ToLower().Replace(" ", "")}@charm.contact";

					_logger.LogInformation($"   👤 {username} | Email: {email} | ClickUp ID: {clickUpId}");

					// 🔥 SYNC VÀO BACKEND (CREATE OR UPDATE)
					var backendUserId = await EnsureUserExistsInBackend(username, email, clickUpId);

					// 🔥 THÊM VÀO DANH SÁCH VỚI ID TỪ BACKEND
					users.Add(new
					{
						id = backendUserId, // 🔥 DÙNG ID TỪ BACKEND, KHÔNG TỰ ĐẾM
						name = username,
						email = email,
						username = username,
						clickup_id = clickUpId
					});
				}

				_logger.LogInformation($"✅ [USERS] Successfully fetched and synced {users.Count} users");
				return users;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [USERS] GetUsersFromClickUp error: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
				return null;
			}
		}

		// 🔥 THÊM 2 HELPER METHODS MỚI (THÊM VÀO CUỐI CLASS)

		/// <summary>
		/// Đảm bảo user tồn tại trong backend database, trả về user ID
		/// </summary>
		private async Task<int> EnsureUserExistsInBackend(string name, string email, string clickUpId)
		{
			try
			{
				// 1. Kiểm tra user đã tồn tại chưa
				var existingUser = await TryGetUserByClickUpId(clickUpId);

				if (existingUser.HasValue)
				{
					var userId = existingUser.Value.GetProperty("id").GetInt32();
					_logger.LogDebug($"✅ [SYNC] User already exists: {name} (ID={userId})");
					return userId;
				}

				// 2. Thử tìm bằng email
				existingUser = await TryGetUserByEmail(email);
				if (existingUser.HasValue)
				{
					var userId = existingUser.Value.GetProperty("id").GetInt32();
					_logger.LogInformation($"✅ [SYNC] User found by email: {name} (ID={userId})");

					// Update clickup_id nếu chưa có
					try
					{
						await _api.PutAsync($"api/v1/users/{userId}", new { clickup_id = clickUpId });
						_logger.LogInformation($"✅ [SYNC] Updated clickup_id for user {userId}");
					}
					catch { }

					return userId;
				}

				// 3. Tạo mới user
				_logger.LogInformation($"🔄 [SYNC] Creating new user in backend: {name}");

				var payload = new
				{
					name = name,
					email = email,
					password = "ClickUpSync123!",
					clickup_id = clickUpId
				};

				var result = await _api.PostAsync("api/v1/users", payload);

				if (!string.IsNullOrEmpty(result))
				{
					var createdUser = JsonDocument.Parse(result).RootElement;
					var newUserId = createdUser.GetProperty("id").GetInt32();
					_logger.LogInformation($"✅ [SYNC] User created: {name} (ID={newUserId})");
					return newUserId;
				}

				// Fallback: return 1 nếu tạo thất bại
				_logger.LogWarning($"⚠️ [SYNC] Failed to create user, using fallback ID=1");
				return 1;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [SYNC] EnsureUserExistsInBackend error: {ex.Message}");
				return 1;
			}
		}

		/// <summary>
		/// Tìm user trong backend bằng clickup_id
		/// </summary>
		private async Task<JsonElement?> TryGetUserByClickUpId(string clickUpId)
		{
			try
			{
				if (string.IsNullOrEmpty(clickUpId))
					return null;

				var response = await _api.GetAsync($"api/v1/users?clickup_id={clickUpId}");

				if (!string.IsNullOrEmpty(response))
				{
					var result = JsonDocument.Parse(response).RootElement;

					if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
						return result[0];

					if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("id", out _))
						return result;
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Tìm user trong backend bằng email
		/// </summary>
		private async Task<JsonElement?> TryGetUserByEmail(string email)
		{
			try
			{
				if (string.IsNullOrEmpty(email))
					return null;

				var response = await _api.GetAsync($"api/v1/users?email={Uri.EscapeDataString(email)}");

				if (!string.IsNullOrEmpty(response))
				{
					var result = JsonDocument.Parse(response).RootElement;

					if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
						return result[0];

					if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("id", out _))
						return result;
				}

				return null;
			}
			catch
			{
				return null;
			}
		}
	}
}