using System.Text.Json;
using System.Globalization;
using System.Text;

namespace AIHubTaskDashboard.Services
{
	public class UserMappingService
	{
		private readonly ApiClientService _apiClient;
		private readonly HttpClient _httpClient;
		private readonly string _clickUpToken;
		private readonly string _teamId;
		private readonly ILogger<UserMappingService> _logger;

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

				// 🔥 FALLBACK: Thử tìm trong Dashboard xem có clickup_id không
				var dashboardUserInfo = await GetDashboardUserInfo(dashboardUserId);
				if (dashboardUserInfo != null &&
					dashboardUserInfo.Value.TryGetProperty("clickup_id", out var clickupIdProp))
				{
					var clickupId = clickupIdProp.ValueKind == JsonValueKind.Number
						? clickupIdProp.GetInt64().ToString()
						: clickupIdProp.GetString();

					if (!string.IsNullOrEmpty(clickupId))
					{
						_logger.LogInformation($"✅ [MAPPING] Found clickup_id in Dashboard user: {clickupId}");
						return clickupId;
					}
				}

				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] MapDashboardUserToClickUp error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Lấy thông tin user từ Dashboard
		/// </summary>
		private async Task<JsonElement?> GetDashboardUserInfo(int userId)
		{
			try
			{
				_logger.LogInformation($"🌐 [MAPPING] Fetching Dashboard user info: {userId}");

				var endpoints = new[] { $"api/v1/members/{userId}", $"api/v1/users/{userId}" };

				foreach (var endpoint in endpoints)
				{
					try
					{
						var response = await _apiClient.GetAsync(endpoint);
						if (!string.IsNullOrEmpty(response))
						{
							var user = JsonDocument.Parse(response).RootElement;
							_logger.LogInformation($"✅ [MAPPING] Found Dashboard user {userId}");
							return user;
						}
					}
					catch { continue; }
				}

				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetDashboardUserInfo error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Lấy thông tin user từ ClickUp API
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
		/// Lấy mapping giữa ClickUp và Dashboard users - IMPROVED VERSION với LOCAL PRIORITY
		/// Key: ClickUp user ID, Value: Dashboard user ID
		/// </summary>
		private async Task<Dictionary<string, int>> GetUserMapping()
		{
			if (_cachedMapping != null && DateTime.UtcNow - _lastCacheUpdate < _cacheExpiry)
			{
				_logger.LogInformation($"✅ [MAPPING] Using cached mapping ({_cachedMapping.Count} entries)");
				return _cachedMapping;
			}

			try
			{
				_logger.LogInformation("🔄 [MAPPING] Building user mapping...");

				var clickUpUsers = await GetClickUpUsers();
				_logger.LogInformation($"📥 [MAPPING] Fetched {clickUpUsers.Count} users from ClickUp");

				var dashboardUsers = await GetDashboardUsers();
				_logger.LogInformation($"📥 [MAPPING] Fetched {dashboardUsers.Count} users from Dashboard");

				var mapping = new Dictionary<string, int>();
				var unmappedClickUpUsers = new List<string>();

				foreach (var cuUser in clickUpUsers)
				{
					string clickUpId = "";
					if (cuUser.TryGetProperty("id", out var idProp))
					{
						clickUpId = idProp.ValueKind == JsonValueKind.Number
							? idProp.GetInt64().ToString()
							: (idProp.GetString() ?? "");
					}

					var clickUpEmail = GetPropertySafe(cuUser, "email")?.ToLower()?.Trim();
					var clickUpUsername = GetPropertySafe(cuUser, "username")?.ToLower()?.Trim();

					if (string.IsNullOrEmpty(clickUpId))
					{
						_logger.LogWarning($"⚠️ [MAPPING] Skipping user with empty ID");
						continue;
					}

					bool mapped = false;

					foreach (var dbUser in dashboardUsers)
					{
						var dbId = dbUser.GetProperty("id").GetInt32();
						var dbEmail = GetPropertySafe(dbUser, "email")?.ToLower()?.Trim();
						var dbName = GetPropertySafe(dbUser, "name")?.ToLower()?.Trim();
						var dbUsername = GetPropertySafe(dbUser, "username")?.ToLower()?.Trim();

						// 🔥 PRIORITY 1: Exact match by clickup_id field
						if (dbUser.TryGetProperty("clickup_id", out var dbClickUpId))
						{
							var dbClickUpIdStr = dbClickUpId.ValueKind == JsonValueKind.Number
								? dbClickUpId.GetInt64().ToString()
								: dbClickUpId.GetString();

							if (!string.IsNullOrEmpty(dbClickUpIdStr) && dbClickUpIdStr == clickUpId)
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by clickup_id: {clickUpId} → Dashboard:{dbId}");
								mapped = true;
								break;
							}
						}

						// 🔥 PRIORITY 2: Exact email match
						if (!string.IsNullOrEmpty(clickUpEmail) && !string.IsNullOrEmpty(dbEmail))
						{
							if (clickUpEmail == dbEmail)
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by email: {clickUpEmail} | ClickUp:{clickUpId} → Dashboard:{dbId}");
								mapped = true;
								break;
							}
						}

						// 🔥 PRIORITY 3: Email domain match (same prefix before @)
						if (!string.IsNullOrEmpty(clickUpEmail) && !string.IsNullOrEmpty(dbEmail))
						{
							if (clickUpEmail.Contains("@") && dbEmail.Contains("@"))
							{
								var cuPrefix = clickUpEmail.Split('@')[0];
								var dbPrefix = dbEmail.Split('@')[0];

								if (cuPrefix == dbPrefix)
								{
									mapping[clickUpId] = dbId;
									_logger.LogInformation($"✅ [MAPPING] Mapped by email prefix: {cuPrefix} | ClickUp:{clickUpId} → Dashboard:{dbId}");
									mapped = true;
									break;
								}
							}
						}

						// 🔥 PRIORITY 4: Username exact match
						if (!string.IsNullOrEmpty(clickUpUsername) && !string.IsNullOrEmpty(dbUsername))
						{
							if (clickUpUsername == dbUsername)
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by username: {clickUpUsername} | ClickUp:{clickUpId} → Dashboard:{dbId}");
								mapped = true;
								break;
							}
						}

						// 🔥 PRIORITY 5: Fuzzy name matching
						if (!string.IsNullOrEmpty(clickUpUsername) && !string.IsNullOrEmpty(dbName))
						{
							var cleanClickUp = NormalizeName(clickUpUsername);
							var cleanDb = NormalizeName(dbName);

							if (cleanClickUp.Contains(cleanDb) || cleanDb.Contains(cleanClickUp))
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by name fuzzy: '{dbName}' ≈ '{clickUpUsername}' | ClickUp:{clickUpId} → Dashboard:{dbId}");
								mapped = true;
								break;
							}

							var cuWords = cleanClickUp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
							var dbWords = cleanDb.Split(' ', StringSplitOptions.RemoveEmptyEntries);

							int matchCount = cuWords.Intersect(dbWords).Count();
							if (matchCount >= 2 || (matchCount == 1 && (cuWords.Length == 1 || dbWords.Length == 1)))
							{
								mapping[clickUpId] = dbId;
								_logger.LogInformation($"✅ [MAPPING] Mapped by word match ({matchCount} words): '{dbName}' ≈ '{clickUpUsername}' | ClickUp:{clickUpId} → Dashboard:{dbId}");
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

				var data = JsonDocument.Parse(content).RootElement;
				var team = data.GetProperty("team");
				var members = team.GetProperty("members");

				var users = new List<JsonElement>();
				foreach (var member in members.EnumerateArray())
				{
					var user = member.GetProperty("user");

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
		/// Lấy danh sách users từ Dashboard - ƯU TIÊN UsersController (ClickUp users)
		/// </summary>
		private async Task<List<JsonElement>> GetDashboardUsers()
		{
			try
			{
				_logger.LogInformation("🌐 [MAPPING] Fetching Dashboard users");

				// 🔥 THAY ĐỔI: Lấy từ UsersController LOCAL thay vì backend API
				try
				{
					_logger.LogInformation("🔄 [MAPPING] Trying LOCAL UsersController first");

					// Tạo HTTP client để gọi local endpoint
					using var localClient = new HttpClient();
					localClient.BaseAddress = new Uri("http://localhost:5076/"); // hoặc https://localhost:7291/
					localClient.Timeout = TimeSpan.FromSeconds(10);

					var localResponse = await localClient.GetAsync("api/v1/users"); // 🔥 ĐỔI TÊN
					var content = await localResponse.Content.ReadAsStringAsync();

					if (localResponse.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
					{
						var data = JsonDocument.Parse(content).RootElement;
						var users = new List<JsonElement>();

						if (data.ValueKind == JsonValueKind.Array)
						{
							foreach (var user in data.EnumerateArray())
							{
								var userId = user.GetProperty("id").GetInt32();
								var email = GetPropertySafe(user, "email");
								var name = GetPropertySafe(user, "name");
								var clickupId = GetPropertySafe(user, "clickup_id");

								_logger.LogInformation($"   👤 Local User: ID={userId}, Email={email}, Name={name}, ClickUpID={clickupId}");
								users.Add(user);
							}
						}

						if (users.Count > 0)
						{
							_logger.LogInformation($"✅ [MAPPING] Fetched {users.Count} users from LOCAL UsersController");
							return users;
						}
					}
				}
				catch (Exception localEx)
				{
					_logger.LogWarning($"⚠️ [MAPPING] Local UsersController failed: {localEx.Message}");
				}

				// Fallback: Thử backend API
				string backendResponse = null; // 🔥 ĐỔI TÊN
				var endpoints = new[] { "api/v1/users", "api/v1/members" };

				foreach (var endpoint in endpoints)
				{
					try
					{
						_logger.LogInformation($"🔄 [MAPPING] Trying backend endpoint: {endpoint}");
						backendResponse = await _apiClient.GetAsync(endpoint); // 🔥 ĐỔI TÊN

						if (!string.IsNullOrEmpty(backendResponse))
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

				if (string.IsNullOrEmpty(backendResponse))
				{
					_logger.LogError("❌ [MAPPING] All endpoints failed");
					return new List<JsonElement>();
				}

				var backendData = JsonDocument.Parse(backendResponse).RootElement; // 🔥 ĐỔI TÊN
				var backendUsers = new List<JsonElement>();

				if (backendData.ValueKind == JsonValueKind.Array)
				{
					foreach (var user in backendData.EnumerateArray())
					{
						var userId = user.GetProperty("id").GetInt32();
						var email = GetPropertySafe(user, "email");
						var name = GetPropertySafe(user, "name");

						_logger.LogInformation($"   👤 Backend User: ID={userId}, Email={email}, Name={name}");
						backendUsers.Add(user);
					}
				}

				_logger.LogInformation($"✅ [MAPPING] Fetched {backendUsers.Count} users from backend");
				return backendUsers;
			}
			catch (Exception ex)
			{
				_logger.LogError($"❌ [MAPPING] GetDashboardUsers error: {ex.Message}");
				_logger.LogError($"   StackTrace: {ex.StackTrace}");
				return new List<JsonElement>();
			}
		}


		/// <summary>
		/// Normalize name for fuzzy matching (remove diacritics, extra spaces, lowercase)
		/// </summary>
		private string NormalizeName(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return "";

			// Remove diacritics
			var normalized = text.Normalize(NormalizationForm.FormD);
			var sb = new StringBuilder();

			foreach (var c in normalized)
			{
				var uc = CharUnicodeInfo.GetUnicodeCategory(c);
				if (uc != UnicodeCategory.NonSpacingMark)
				{
					sb.Append(c);
				}
			}

			var result = sb.ToString()
				.Normalize(NormalizationForm.FormC)
				.ToLower()
				.Trim();

			// Normalize whitespace
			result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

			return result;
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
		/// Export mapping để debug
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