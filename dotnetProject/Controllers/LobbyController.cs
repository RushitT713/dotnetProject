using dotnetProject.Services; // --- ADD THIS ---
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks; // --- ADD THIS ---

namespace dotnetProject.Controllers
{
    public class LobbyController : Controller
    {
        // --- START FIX ---
        private readonly IWalletService _walletService;

        public LobbyController(IWalletService walletService)
        {
            _walletService = walletService;
        }

        private string GetPlayerId()
        {
            return HttpContext.Items["PlayerId"]?.ToString() ?? string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var playerId = GetPlayerId();
            if (!string.IsNullOrEmpty(playerId))
            {
                var player = await _walletService.GetOrCreatePlayerAsync(playerId);
                ViewBag.PlayerName = player.DisplayName;
            }

            return View();
        }
        // --- END FIX ---
    }
}