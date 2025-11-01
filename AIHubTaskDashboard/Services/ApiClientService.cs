using System.Net.Http.Json;
using System.Text.Json;

namespace AIHubTaskDashboard.Services
{
	public class ApiClientService
	{
		private readonly HttpClient _httpClient;
		private readonly IHttpContextAccessor _contextAccessor;

		public ApiClientService(HttpClient httpClient, IConfiguration config, IHttpContextAccessor accessor)
		{
			_httpClient = httpClient;
			_contextAccessor = accessor;
			_httpClient.BaseAddress = new Uri(config["ApiSettings:BaseUrl"]!);
		}

		private void AddAuthHeader()
		{
			var token = _contextAccessor.HttpContext?.Session.GetString("AuthToken");
			_httpClient.DefaultRequestHeaders.Authorization =
				token != null ? new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) : null;
		}

		public async Task<string> GetAsync(string endpoint)
		{
			AddAuthHeader();
			var response = await _httpClient.GetAsync(endpoint);
			var content = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"GET {endpoint} failed: {response.StatusCode} - {content}");

			return content;
		}

		public async Task<string> PostAsync(string endpoint, object data)
		{
			AddAuthHeader();
			var response = await _httpClient.PostAsJsonAsync(endpoint, data);
			var content = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"POST {endpoint} failed: {response.StatusCode} - {content}");

			return content;
		}

		public async Task<string> PutAsync(string endpoint, object data)
		{
			AddAuthHeader();
			var response = await _httpClient.PutAsJsonAsync(endpoint, data);
			var content = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"PUT {endpoint} failed: {response.StatusCode} - {content}");

			return content;
		}

		public async Task<string> PatchAsync(string endpoint, object data)
		{
			AddAuthHeader();
			var response = await _httpClient.PatchAsJsonAsync(endpoint, data);
			var content = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"PATCH {endpoint} failed: {response.StatusCode} - {content}");

			return content;
		}

		public async Task<string> DeleteAsync(string endpoint)
		{
			AddAuthHeader();
			var response = await _httpClient.DeleteAsync(endpoint);
			var content = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"DELETE {endpoint} failed: {response.StatusCode} - {content}");

			return content;
		}
	}
}
