using System.Net.Http.Json;
using System.Text.Json;

namespace AIHubTaskDashboard.Services
{
	public class ApiClientService
	{
		private readonly HttpClient _httpClient;
		private readonly IHttpContextAccessor _contextAccessor;
		private readonly ILogger<ApiClientService> _logger;

		public ApiClientService(
			HttpClient httpClient,
			IConfiguration config,
			IHttpContextAccessor accessor,
			ILogger<ApiClientService> logger)
		{
			_httpClient = httpClient;
			_contextAccessor = accessor;
			_logger = logger;

			_httpClient.BaseAddress = new Uri(config["ApiSettings:BaseUrl"]!);
			_httpClient.Timeout = TimeSpan.FromSeconds(90);

			_logger.LogInformation($"🔧 [API CLIENT] Initialized - Base: {_httpClient.BaseAddress}, Timeout: {_httpClient.Timeout.TotalSeconds}s");
		}

		private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
		{
			var request = new HttpRequestMessage(method, endpoint);

			var token = _contextAccessor.HttpContext?.Session.GetString("AuthToken");
			if (!string.IsNullOrEmpty(token))
			{
				request.Headers.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
			}

			return request;
		}

		public async Task<string> GetAsync(string endpoint)
		{
			try
			{
				_logger.LogInformation($"📥 [GET] {endpoint}");

				var request = CreateRequest(HttpMethod.Get, endpoint);
				var response = await _httpClient.SendAsync(request);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [GET] {endpoint} - {response.StatusCode}");
					throw new Exception($"GET {endpoint} failed: {response.StatusCode} - {content}");
				}

				return content;
			}
			catch (ObjectDisposedException ex)
			{
				_logger.LogError($"❌ [GET] HttpClient disposed: {ex.Message}");
				throw;
			}
		}

		public async Task<string> PostAsync(string endpoint, object data)
		{
			try
			{
				_logger.LogInformation($"📤 [POST] {endpoint} - Timeout: {_httpClient.Timeout.TotalSeconds}s");

				var request = CreateRequest(HttpMethod.Post, endpoint);
				request.Content = JsonContent.Create(data);

				var response = await _httpClient.SendAsync(request);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [POST] {endpoint} - {response.StatusCode}");
					_logger.LogError($"❌ [POST] Response: {content}");
					throw new Exception($"POST {endpoint} failed: {response.StatusCode} - {content}");
				}

				_logger.LogInformation($"✅ [POST] {endpoint} - Success");
				return content;
			}
			catch (ObjectDisposedException ex)
			{
				_logger.LogError($"❌ [POST] HttpClient disposed: {ex.Message}");
				throw;
			}
		}

		public async Task<string> PutAsync(string endpoint, object data)
		{
			try
			{
				_logger.LogInformation($"📤 [PUT] {endpoint}");

				var request = CreateRequest(HttpMethod.Put, endpoint);
				request.Content = JsonContent.Create(data);

				var response = await _httpClient.SendAsync(request);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [PUT] {endpoint} - {response.StatusCode}");
					throw new Exception($"PUT {endpoint} failed: {response.StatusCode} - {content}");
				}

				return content;
			}
			catch (ObjectDisposedException ex)
			{
				_logger.LogError($"❌ [PUT] HttpClient disposed: {ex.Message}");
				throw;
			}
		}

		public async Task<string> PatchAsync(string endpoint, object data)
		{
			try
			{
				_logger.LogInformation($"📤 [PATCH] {endpoint}");

				var request = CreateRequest(HttpMethod.Patch, endpoint);
				request.Content = JsonContent.Create(data);

				var response = await _httpClient.SendAsync(request);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [PATCH] {endpoint} - {response.StatusCode}");
					throw new Exception($"PATCH {endpoint} failed: {response.StatusCode} - {content}");
				}

				return content;
			}
			catch (ObjectDisposedException ex)
			{
				_logger.LogError($"❌ [PATCH] HttpClient disposed: {ex.Message}");
				throw;
			}
		}

		public async Task<string> DeleteAsync(string endpoint)
		{
			try
			{
				_logger.LogInformation($"🗑️ [DELETE] {endpoint}");

				var request = CreateRequest(HttpMethod.Delete, endpoint);
				var response = await _httpClient.SendAsync(request);
				var content = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError($"❌ [DELETE] {endpoint} - {response.StatusCode}");
					throw new Exception($"DELETE {endpoint} failed: {response.StatusCode} - {content}");
				}

				return content;
			}
			catch (ObjectDisposedException ex)
			{
				_logger.LogError($"❌ [DELETE] HttpClient disposed: {ex.Message}");
				throw;
			}
		}
	}
}