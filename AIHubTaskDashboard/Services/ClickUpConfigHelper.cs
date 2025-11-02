namespace AIHubTaskDashboard.Helpers
{
	/// <summary>
	/// Helper class để đọc ClickUp config dễ dàng
	/// </summary>
	public class ClickUpConfigHelper
	{
		private readonly IConfiguration _config;

		public ClickUpConfigHelper(IConfiguration config)
		{
			_config = config;
		}

		// Basic settings
		public string Token => _config["ClickUpSettings:Token"] ?? "";
		public string ListId => _config["ClickUpSettings:ListId"] ?? "";
		public string SpaceId => _config["ClickUpSettings:SpaceId"] ?? "";
		public string TeamId => _config["ClickUpSettings:TeamId"] ?? "";
		public string ApiBaseUrl => _config["ClickUpSettings:ApiBaseUrl"] ?? "https://api.clickup.com/api/v2/";

		// Background sync settings
		public bool EnableBackgroundSync => _config.GetValue<bool>("ClickUpSettings:EnableBackgroundSync", true);
		public int SyncIntervalMinutes => _config.GetValue<int>("ClickUpSettings:SyncIntervalMinutes", 3);

		// Webhook settings
		public bool FastResponseMode => _config.GetValue<bool>("ClickUpSettings:WebhookSettings:FastResponseMode", true);
		public int MaxProcessingTimeSeconds => _config.GetValue<int>("ClickUpSettings:WebhookSettings:MaxProcessingTimeSeconds", 2);
		public bool BackgroundProcessing => _config.GetValue<bool>("ClickUpSettings:WebhookSettings:BackgroundProcessing", true);

		// Rate limiting
		public int MaxConcurrentRequests => _config.GetValue<int>("ClickUpSettings:RateLimiting:MaxConcurrentRequests", 5);
		public int DelayBetweenRequestsMs => _config.GetValue<int>("ClickUpSettings:RateLimiting:DelayBetweenRequestsMs", 100);
		public int MaxTasksToSync => _config.GetValue<int>("ClickUpSettings:RateLimiting:MaxTasksToSync", 30);
		public bool EnableThrottling => _config.GetValue<bool>("ClickUpSettings:RateLimiting:EnableThrottling", true);

		// Performance settings
		public bool EnableTagsCache => _config.GetValue<bool>("ClickUpSettings:PerformanceSettings:EnableTagsCache", true);
		public int TagsCacheMinutes => _config.GetValue<int>("ClickUpSettings:PerformanceSettings:TagsCacheMinutes", 3);
		public bool EnableFastLoad => _config.GetValue<bool>("ClickUpSettings:PerformanceSettings:EnableFastLoad", true);
		public bool SkipTagsByDefault => _config.GetValue<bool>("ClickUpSettings:PerformanceSettings:SkipTagsByDefault", false);
		public int MaxTagsFetchTimeout => _config.GetValue<int>("ClickUpSettings:PerformanceSettings:MaxTagsFetchTimeout", 10);

		// Telegram settings
		public bool TelegramEnabled => _config.GetValue<bool>("Telegram:EnableNotifications", true);
		public bool NotifyOnSync => _config.GetValue<bool>("Telegram:NotifyOnSync", false);
		public bool NotifyOnError => _config.GetValue<bool>("Telegram:NotifyOnError", true);
		public string TelegramBotToken => _config["Telegram:BotToken"] ?? "";
		public string TelegramChatId => _config["Telegram:ChatId"] ?? "";

		// Validation
		public bool IsValid()
		{
			return !string.IsNullOrEmpty(Token) &&
				   !string.IsNullOrEmpty(ListId) &&
				   !string.IsNullOrEmpty(SpaceId);
		}

		// Get all settings as string for logging
		public string GetConfigSummary()
		{
			return $@"
ClickUp Configuration:
- API Base URL: {ApiBaseUrl}
- List ID: {ListId}
- Space ID: {SpaceId}
- Team ID: {TeamId}
- Token: {Token.Substring(0, Math.Min(15, Token.Length))}...
- Background Sync: {EnableBackgroundSync} (Interval: {SyncIntervalMinutes} min)
- Fast Response Mode: {FastResponseMode}
- Max Concurrent Requests: {MaxConcurrentRequests}
- Max Tasks to Sync: {MaxTasksToSync}
- Tags Cache: {EnableTagsCache} ({TagsCacheMinutes} min)
- Fast Load: {EnableFastLoad}
- Telegram Notifications: {TelegramEnabled}
";
		}
	}
}