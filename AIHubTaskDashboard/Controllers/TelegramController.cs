using AIHUBOS.Dashboard.Services;
using AIHubTaskDashboard.DTOS;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace AIHUBOS.Dashboard.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramController : ControllerBase
    {
        private readonly TelegramService _telegram;

        public TelegramController(TelegramService telegram)
        {
            _telegram = telegram;
        }

        [HttpPost("log")]
        public async Task<IActionResult> SendLog([FromBody] TelegramLogDto dto)
        {
            string msg =
                $" *{dto.Source} Log*\n" +
                $" Action: {dto.Action}\n" +
                $" {dto.Message}";

            await _telegram.SendMessageAsync(msg);
            return Ok(new { success = true });
        }
    }
}
