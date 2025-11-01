using System.Text.Json;
using AIHUBOS.Dashboard.Services;

public class TelegramLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TelegramService _telegram;

    public TelegramLogMiddleware(RequestDelegate next, TelegramService telegram)
    {
        _next = next;
        _telegram = telegram;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;

        await _next(context); // gọi tiếp pipeline

        // Log nếu là API thay đổi dữ liệu
        if (path.StartsWith("/api/") &&
            (method == "POST" || method == "PUT" || method == "DELETE"))
        {
            var status = context.Response.StatusCode;
            await _telegram.SendMessageAsync(
                $" *API Update*\n" +
                $" Path: `{path}`\n" +
                $" Method: {method}\n" +
                $" Status: {status}"
            );
        }
    }
}
