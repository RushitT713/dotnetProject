using dotnetProject.Models;
using dotnetProject.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dotnetProject.Controllers
{
    public class WalletController : Controller
    {
        private readonly IWalletService _walletService;
        private readonly ILogger<WalletController> _logger;
        private const decimal AdRewardAmount = 100m;
        public WalletController(IWalletService walletService, ILogger<WalletController> logger)
        {
            _walletService = walletService;
            _logger = logger;
        }

        private string GetPlayerId()
        {
            // Gets the PlayerId set by the PlayerIdentificationMiddleware
            return HttpContext.Items["PlayerId"]?.ToString() ?? string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var playerId = GetPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                // This shouldn't happen if the middleware is set up, but good to check
                return RedirectToAction("Index", "Lobby");
            }

            // Get balance and history from your service
            var balance = await _walletService.GetBalanceAsync(playerId);
            var transactions = await _walletService.GetTransactionHistoryAsync(playerId, 100); // Get last 100

            // Create the view model
            var viewModel = new WalletViewModel
            {
                CurrentBalance = balance,
                History = transactions
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> WatchAdReward()
        {
            var playerId = GetPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                return Json(new { success = false, error = "Player not found." });
            }

            // Add the reward using the wallet service
            bool success = await _walletService.AddBalanceAsync(
                playerId,
                AdRewardAmount,
                "General",
                $"Ad Watched: +₹{AdRewardAmount}"
            );

            if (success)
            {
                var newBalance = await _walletService.GetBalanceAsync(playerId);
                return Json(new { success = true, newBalance = newBalance, reward = AdRewardAmount });
            }

            return Json(new { success = false, error = "Failed to add balance." });
        }
    }
}