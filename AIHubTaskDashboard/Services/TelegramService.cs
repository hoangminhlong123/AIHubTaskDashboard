using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIHUBOS.Dashboard.Services
{
    public class TelegramService
    {
        private readonly string _botToken;
        private readonly string _chatId;
        private readonly HttpClient _httpClient;

        public TelegramService(IConfiguration config)
        {
            _botToken = config["Telegram:BotToken"];
            _chatId = config["Telegram:ChatId"];
            _httpClient = new HttpClient();
        }

        public async Task SendMessageAsync(string message)
        {
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "Markdown"
            };
            var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(url, json);
        }
    }
}
