using System.Text.Json;

namespace AIHubTaskDashboard.Services
{
	public class UserMappingService
	{
		private readonly ApiClientService _apiClient;
		private readonly HttpClient _httpClient;
		private readonly string _clickUpToken;
		private readonly string _teamId;
		private readonly ILogger<UserMappingService> _logger;

		// Cache để tránh gọi API liên tục
		private Dictionary<string, int>? _cachedMapping;
		private DateTime _lastCacheUpdate = DateTime.MinValue;
		private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(10);

		public UserMappingService(
			ApiClientService apiClient,
			IConfiguration config,
			ILogger<UserMappingService> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_httpClient = new HttpClient();
			_clickUpToken = config["ClickUpSettings:Token"] ?? "";
			_teamId = config["ClickUpSettings:TeamId"] ?? "90181891084";

			var baseUrl = config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";
			_httpClient.BaseAddress = new Uri(baseUrl);
			_httpClient.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue(_clickUpToken);
			_httpClient.DefaultRequestHeaders.Add("User-Agent", "AIHubTaskDashboard");
		}

		/// <summary>
		/// Map ClickUp user ID sang Dashboard user ID
		/// </summary>
		public async Task<int?> MapClickUpUserToDashboard(string clickUpUserId)
		{
			try
			{
				_logger.LogInformation($"🔍 [MAPPING] Mapping ClickUp user: {clickUpUserId}");

				var mapping = await GetUserMapping();

				if (mapping.TryGetValue(clickUpUserId, out var dashboardUserId))
				{
					_logger.LogInformation($"✅ [MAPPING] Mapped ClickUp user {clickUpUserId} → Dashboard user {dashboardUserId}");
					return dashboardUserId;
				}

				_logger.LogWarning($"⚠️ [MAPPING] No mapping found for ClickUp user: {clickUpUserId}");

				// 🔥 Fallback: Tìm kiếm user từ ClickUp API để lấy thêm thông tin
				var clickUpUserInfo = await GetClickUpUserInfo(clickUpUserId);

				if (clickUpUserInfo != null)
				{
					var email = GetPropertySafe(clickUpUserInfo.Value, "email");
					var username = GetPropertySafe(clickUpUserInfo.Value, "username");
					_logger.LogInformation($"📋 [MAPPING] ClickUp user info - Email: {email}, Username: {username}");
				}

				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] MapClickUpUserToDashboard error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Map Dashboard user ID sang ClickUp user ID
		/// </summary>
		public async Task<string?> MapDashboardUserToClickUp(int dashboardUserId)
		{
			try
			{
				_logger.LogInformation($"🔍 [MAPPING] Mapping Dashboard user: {dashboardUserId}");

				var mapping = await GetUserMapping();

				foreach (var kvp in mapping)
				{
					if (kvp.Value == dashboardUserId)
					{
						_logger.LogInformation($"✅ [MAPPING] Mapped Dashboard user {dashboardUserId} → ClickUp user {kvp.Key}");
						return kvp.Key;
					}
				}

				_logger.LogWarning($"⚠️ [MAPPING] No mapping found for Dashboard user: {dashboardUserId}");
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] MapDashboardUserToClickUp error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Lấy thông tin user từ ClickUp API (fallback khi không có trong cache)
		/// </summary>
		private async Task<JsonElement?> GetClickUpUserInfo(string userId)
		{
			try
			{
				_logger.LogInformation($"🌐 [MAPPING] Fetching user info from ClickUp: {userId}");

				var response = await _httpClient.GetAsync($"user/{userId}");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [MAPPING] Failed to fetch user: {response.StatusCode}");
					return null;
				}

				var data = JsonDocument.Parse(content).RootElement;
				return data.GetProperty("user");
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetClickUpUserInfo error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Lấy mapping giữa ClickUp và Dashboard users
		/// Key: ClickUp user ID, Value: Dashboard user ID
		/// </summary>
		private async Task<Dictionary<string, int>> GetUserMapping()
		{
			// Kiểm tra cache
			if (_cachedMapping != null && DateTime.UtcNow - _lastCacheUpdate < _cacheExpiry)
			{
				_logger.LogInformation($"✅ [MAPPING] Using cached mapping ({_cachedMapping.Count} entries)");
				return _cachedMapping;
			}

			try
			{
				_logger.LogInformation("🔄 [MAPPING] Building user mapping...");

				// 1️⃣ Lấy users từ ClickUp
				var clickUpUsers = await GetClickUpUsers();
				_logger.LogInformation($"📥 [MAPPING] Fetched {clickUpUsers.Count} users from ClickUp");

				// 2️⃣ Lấy users từ Dashboard
				var dashboardUsers = await GetDashboardUsers();
				_logger.LogInformation($"📥 [MAPPING] Fetched {dashboardUsers.Count} users from Dashboard");

				// 3️⃣ Tạo mapping dựa trên email/username
				var mapping = new Dictionary<string, int>();
				var unmappedClickUpUsers = new List<string>();

				foreach (var cuUser in clickUpUsers)
				{
					// 🔥 XỬ LÝ ID ĐÚNG KIỂU (số hoặc string)
					string clickUpId = "";
					if (cuUser.TryGetProperty("id", out var idProp))
					{
						clickUpId = idProp.ValueKind == JsonValueKind.Number
							? idProp.GetInt64().ToString()
							: (idProp.GetString() ?? "");
					}

					var clickUpEmail = GetPropertySafe(cuUser, "email")?.ToLower();
					var clickUpUsername = GetPropertySafe(cuUser, "username")?.ToLower();

					if (string.IsNullOrEmpty(clickUpId))
					{
						_logger.LogWarning($"⚠️ [MAPPING] Skipping user with empty ID");
						continue;
					}

					bool mapped = false;

					// Tìm user tương ứng trong Dashboard
					foreach (var dbUser in dashboardUsers)
					{
						var dbId = dbUser.GetProperty("id").GetInt32();
						var dbEmail = GetPropertySafe(dbUser, "email")?.ToLower();
						var dbName = GetPropertySafe(dbUser, "name")?.ToLower();
						var dbUsername = GetPropertySafe(dbUser, "username")?.ToLower();

						// Match theo email (ưu tiên cao nhất)
						if (!string.IsNullOrEmpty(clickUpEmail) &&
							!string.IsNullOrEmpty(dbEmail) &&
							clickUpEmail == dbEmail)
						{
							mapping[clickUpId] = dbId;
							_logger.LogInformation($"✅ [MAPPING] Mapped by email: {clickUpEmail} | ClickUp:{clickUpId} → Dashboard:{dbId}");
							mapped = true;
							break;
						}

						// Match theo username
						if (!string.IsNullOrEmpty(clickUpUsername) &&
							!string.IsNullOrEmpty(dbUsername) &&
							clickUpUsername == dbUsername)
						{
							mapping[clickUpId] = dbId;
							_logger.LogInformation($"✅ [MAPPING] Mapped by username: {clickUpUsername} | ClickUp:{clickUpId} → Dashboard:{dbId}");
							mapped = true;
							break;
						}

						// Match theo name
						if (!string.IsNullOrEmpty(clickUpUsername) &&
							!string.IsNullOrEmpty(dbName) &&
							clickUpUsername.Contains(dbName))
						{
							mapping[clickUpId] = dbId;
							_logger.LogInformation($"✅ [MAPPING] Mapped by name: {dbName} | ClickUp:{clickUpId} → Dashboard:{dbId}");
							mapped = true;
							break;
						}

						// Match theo email domain (nếu cùng tên trước @)
						if (!string.IsNullOrEmpty(clickUpEmail) &&
							!string.IsNullOrEmpty(dbEmail) &&
							clickUpEmail.Contains("@") && dbEmail.Contains("@"))
						{
							var cuEmailPrefix = clickUpEmail.Split('@')[0];
							var dbEmailPrefix = dbEmail.Split('@')[0];

							if (cuEmailPrefix == dbEmailPrefix)
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by email prefix: {cuEmailPrefix} | ClickUp:{clickUpId} → Dashboard:{dbId}");
								mapped = true;
								break;
							}
						}
					}

					if (!mapped)
					{
						unmappedClickUpUsers.Add($"{clickUpId} ({clickUpEmail ?? clickUpUsername ?? "unknown"})");
					}
				}

				// Log unmapped users
				if (unmappedClickUpUsers.Count > 0)
				{
					_logger.LogWarning($"⚠️ [MAPPING] {unmappedClickUpUsers.Count} ClickUp users could not be mapped:");
					foreach (var user in unmappedClickUpUsers)
					{
						_logger.LogWarning($"   - {user}");
					}
				}

				_cachedMapping = mapping;
				_lastCacheUpdate = DateTime.UtcNow;

				_logger.LogInformation($"✅ [MAPPING] User mapping completed: {mapping.Count} mappings created");
				_logger.LogInformation($"📊 [MAPPING] Summary: {clickUpUsers.Count} ClickUp users, {dashboardUsers.Count} Dashboard users, {mapping.Count} mapped");

				return mapping;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetUserMapping error: {ex.Message}");
				return _cachedMapping ?? new Dictionary<string, int>();
			}
		}

		/// <summary>
		/// Lấy danh sách users từ ClickUp
		/// </summary>
		private async Task<List<JsonElement>> GetClickUpUsers()
		{
			try
			{
				_logger.LogInformation($"🌐 [MAPPING] Fetching ClickUp users from team: {_teamId}");

				var response = await _httpClient.GetAsync($"team/{_teamId}");
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [MAPPING] Failed to fetch ClickUp users: {response.StatusCode} - {content}");
					return new List<JsonElement>();
				}

				_logger.LogInformation($"📦 [MAPPING] ClickUp Response: {content.Substring(0, Math.Min(500, content.Length))}...");

				var data = JsonDocument.Parse(content).RootElement;
				var team = data.GetProperty("team");
				var members = team.GetProperty("members");

				var users = new List<JsonElement>();
				foreach (var member in members.EnumerateArray())
				{
					var user = member.GetProperty("user");

					// 🔥 LOG CHI TIẾT TỪNG USER
					if (user.TryGetProperty("id", out var idProp))
					{
						string userId = idProp.ValueKind == JsonValueKind.Number
							? idProp.GetInt64().ToString()
							: idProp.GetString() ?? "";
						var email = GetPropertySafe(user, "email");
						var username = GetPropertySafe(user, "username");

						_logger.LogInformation($"   👤 ClickUp User: ID={userId}, Email={email}, Username={username}");
					}

					users.Add(user);
				}

				_logger.LogInformation($"✅ [MAPPING] Fetched {users.Count} users from ClickUp");
				return users;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetClickUpUsers error: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
				return new List<JsonElement>();
			}
		}

		/// <summary>
		/// Lấy danh sách users từ Dashboard
		/// </summary>
		private async Task<List<JsonElement>> GetDashboardUsers()
		{
			try
			{
				_logger.LogInformation("🌐 [MAPPING] Fetching Dashboard users");

				string response = null;

				// 🔥 Thử từng endpoint cho đến khi tìm được
				var endpoints = new[] { "api/v1/users", "api/v1/members" };

				foreach (var endpoint in endpoints)
				{
					try
					{
						_logger.LogInformation($"🔄 [MAPPING] Trying endpoint: {endpoint}");
						response = await _apiClient.GetAsync(endpoint);

						if (!string.IsNullOrEmpty(response))
						{
							_logger.LogInformation($"✅ [MAPPING] Successfully fetched from: {endpoint}");
							break;
						}
					}
					catch (Exception apiEx)
					{
						_logger.LogWarning($"⚠️ [MAPPING] Endpoint {endpoint} failed: {apiEx.Message}");
						continue;
					}
				}

				if (string.IsNullOrEmpty(response))
				{
					_logger.LogError("❌ [MAPPING] All endpoints failed");
					return new List<JsonElement>();
				}

				_logger.LogInformation($"📦 [MAPPING] Dashboard Response: {response.Substring(0, Math.Min(500, response.Length))}...");

				var data = JsonDocument.Parse(response).RootElement;
				var users = new List<JsonElement>();

				if (data.ValueKind == JsonValueKind.Array)
				{
					foreach (var user in data.EnumerateArray())
					{
						var userId = user.GetProperty("id").GetInt32();
						var email = GetPropertySafe(user, "email");

						// Dashboard có thể có field "name" thay vì "username"
						var name = GetPropertySafe(user, "name");
						var username = GetPropertySafe(user, "username") ?? name;

						_logger.LogInformation($"   👤 Dashboard User: ID={userId}, Email={email}, Name={name ?? username}");

						users.Add(user);
					}
				}

				_logger.LogInformation($"✅ [MAPPING] Fetched {users.Count} users from Dashboard");
				return users;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetDashboardUsers error: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
				return new List<JsonElement>();
			}
		}

		/// <summary>
		/// Clear cache (gọi khi có thay đổi user)
		/// </summary>
		public void ClearCache()
		{
			_cachedMapping = null;
			_lastCacheUpdate = DateTime.MinValue;
			_logger.LogInformation("🗑️ [MAPPING] User mapping cache cleared");
		}

		/// <summary>
		/// Export mapping để debug (optional)
		/// </summary>
		public async Task<Dictionary<string, object>> GetMappingReport()
		{
			var mapping = await GetUserMapping();

			return new Dictionary<string, object>
			{
				["total_mappings"] = mapping.Count,
				["cache_age_minutes"] = Math.Round((DateTime.UtcNow - _lastCacheUpdate).TotalMinutes, 2),
				["last_updated"] = _lastCacheUpdate.ToString("yyyy-MM-dd HH:mm:ss"),
				["mappings"] = mapping.Select(kvp => new
				{
					clickup_id = kvp.Key,
					dashboard_id = kvp.Value
				}).ToList()
			};
		}

		private string? GetPropertySafe(JsonElement element, string propertyName)
		{
			try
			{
				if (element.TryGetProperty(propertyName, out var prop) &&
					prop.ValueKind != JsonValueKind.Null)
				{
					return prop.GetString();
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