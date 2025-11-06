using AIHUBOS.Dashboard.Services;
using AIHubTaskDashboard.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════
// 📦 ADD SERVICES TO CONTAINER
// ═══════════════════════════════════════════════════════════════════

// Controllers with Views
builder.Services.AddControllersWithViews()
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
		options.JsonSerializerOptions.PropertyNamingPolicy = null;
	});

// HttpContext
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

// HttpClient with proper configuration
builder.Services.AddHttpClient<ApiClientService>(client =>
{
	var baseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "https://aihubtasktracker-z11a.onrender.com/";
	client.BaseAddress = new Uri(baseUrl);
	client.Timeout = TimeSpan.FromSeconds(60);
});

// Session
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromHours(1);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});

// 🔥 FIX: All services must be SCOPED (because UserMappingService needs ApiClientService which is Scoped)
// Singleton services (stateless, thread-safe) - NO DEPENDENCIES ON SCOPED
builder.Services.AddSingleton<TelegramService>();

// 🔥 CRITICAL: Scoped services (per-request lifecycle)
// UserMappingService must be Scoped because it depends on ApiClientService (which is Scoped)
builder.Services.AddScoped<ApiClientService>();
builder.Services.AddScoped<UserMappingService>();  // ✅ CHANGED from Singleton to Scoped
builder.Services.AddScoped<ClickUpService>();      // ✅ For webhook handling
builder.Services.AddScoped<ClickUpApiService>();   // ✅ For API calls
builder.Services.AddHttpClient<ApiClientService>(); // ← Dòng này


// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Logging.AddFilter("AIHubTaskDashboard.Services", LogLevel.Information);
builder.Logging.AddFilter("AIHubTaskDashboard.Controllers", LogLevel.Information);
builder.Logging.AddFilter("AIHUBOS.Dashboard.Services", LogLevel.Information);

// ═══════════════════════════════════════════════════════════════════
// 🚀 BUILD APP
// ═══════════════════════════════════════════════════════════════════

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════════
// 🔧 VERIFY CRITICAL SERVICES ON STARTUP
// ═══════════════════════════════════════════════════════════════════

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("═══════════════════════════════════════════════════════");
logger.LogInformation("🚀 AI Hub Task Dashboard Starting...");
logger.LogInformation($"🌐 Environment: {app.Environment.EnvironmentName}");
logger.LogInformation($"⏰ Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
logger.LogInformation("═══════════════════════════════════════════════════════");

// 🔥 Test service registration (using scope because services are Scoped)
try
{
	using (var scope = app.Services.CreateScope())
	{
		var services = scope.ServiceProvider;

		// Verify ClickUpService
		var clickUpService = services.GetService<ClickUpService>();
		if (clickUpService != null)
		{
			logger.LogInformation("✅ ClickUpService registered successfully");
		}
		else
		{
			logger.LogError("❌ ClickUpService NOT registered!");
		}

		// Verify UserMappingService
		var userMapping = services.GetService<UserMappingService>();
		if (userMapping != null)
		{
			logger.LogInformation("✅ UserMappingService registered successfully");
		}
		else
		{
			logger.LogError("❌ UserMappingService NOT registered!");
		}

		// Verify ClickUpApiService
		var clickUpApi = services.GetService<ClickUpApiService>();
		if (clickUpApi != null)
		{
			logger.LogInformation("✅ ClickUpApiService registered successfully");
		}
		else
		{
			logger.LogError("❌ ClickUpApiService NOT registered!");
		}

		// Verify TelegramService
		var telegram = services.GetService<TelegramService>();
		if (telegram != null)
		{
			logger.LogInformation("✅ TelegramService registered successfully");
		}
		else
		{
			logger.LogError("❌ TelegramService NOT registered!");
		}
	}

	logger.LogInformation("═══════════════════════════════════════════════════════");
	logger.LogInformation("✅ All services verified successfully!");
	logger.LogInformation("═══════════════════════════════════════════════════════");
}
catch (Exception ex)
{
	logger.LogError($"❌ Service verification failed: {ex.Message}");
	logger.LogError($"   StackTrace: {ex.StackTrace}");
}

// ═══════════════════════════════════════════════════════════════════
// 🌐 CONFIGURE HTTP REQUEST PIPELINE
// ═══════════════════════════════════════════════════════════════════

// Exception handling
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Home/Error");
	app.UseHsts();
}
else
{
	app.UseDeveloperExceptionPage();
}

// HTTPS & Static files
app.UseHttpsRedirection();
app.UseStaticFiles();

// Routing
app.UseRouting();

// Session (must be before authorization)
app.UseSession();

// Authorization
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<TelegramLogMiddleware>();

// Map controllers
app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}");

// 🔥 Add explicit webhook route (for clarity)
app.MapControllerRoute(
	name: "webhook",
	pattern: "api/clickup-webhook",
	defaults: new { controller = "ClickUpWebHook", action = "HandleWebhook" });

// ═══════════════════════════════════════════════════════════════════
// 🎯 HEALTH CHECK ENDPOINTS
// ═══════════════════════════════════════════════════════════════════

app.MapGet("/api/health", () =>
{
	logger.LogInformation("✅ [HEALTH] Health check called");
	return Results.Ok(new
	{
		status = "healthy",
		service = "AI Hub Task Dashboard",
		timestamp = DateTime.UtcNow,
		environment = app.Environment.EnvironmentName,
		endpoints = new
		{
			webhook = "/api/clickup-webhook",
			webhook_test = "/api/clickup-webhook/test",
			webhook_health = "/api/clickup-webhook/health",
			dashboard = "/",
			tasks = "/Tasks",
			users = "/api/v1/users"
		}
	});
});

// ═══════════════════════════════════════════════════════════════════
// ✅ RUN APPLICATION
// ═══════════════════════════════════════════════════════════════════

logger.LogInformation("═══════════════════════════════════════════════════════");
logger.LogInformation("🎯 Application configured and ready to start");
logger.LogInformation($"📍 Webhook endpoint: /api/clickup-webhook");
logger.LogInformation("═══════════════════════════════════════════════════════");

app.Run();

logger.LogInformation("═══════════════════════════════════════════════════════");
logger.LogInformation("🛑 AI Hub Task Dashboard Stopped");
logger.LogInformation("═══════════════════════════════════════════════════════");