using dotnetProject.Models;
using dotnetProject.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace dotnetProject.Controllers
{
    public class BlackjackController : Controller
    {
        private const string SessionKey = "BlackjackGame";
        private readonly IWalletService _walletService;

        public BlackjackController(IWalletService walletService)
        {
            _walletService = walletService;
        }

        private string GetPlayerId()
        {
            return HttpContext.Items["PlayerId"]?.ToString() ?? string.Empty;
        }

        private BlackjackGame GetGame()
        {
            var json = HttpContext.Session.GetString(SessionKey);
            BlackjackGame game = json == null
                ? new BlackjackGame()
                : JsonConvert.DeserializeObject<BlackjackGame>(json);

            SaveGame(game);
            return game;
        }

        private void SaveGame(BlackjackGame game)
        {
            var json = JsonConvert.SerializeObject(game);
            HttpContext.Session.SetString(SessionKey, json);
        }

        // Renders /Blackjack
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var playerId = GetPlayerId();
            var game = GetGame();

            // Get real balance from wallet
            var balance = await _walletService.GetBalanceAsync(playerId);
            game.PlayerBalance = (int)balance;
            SaveGame(game);

            return View(game);
        }

        // AJAX: place your bet and deal initial cards
        [HttpPost]
        public async Task<IActionResult> PlaceBet([FromForm] int amount)
        {
            var playerId = GetPlayerId();

            // Check if player has sufficient balance
            if (!await _walletService.HasSufficientBalanceAsync(playerId, amount))
            {
                return Json(new { error = "Insufficient balance" });
            }

            // Deduct bet from wallet
            await _walletService.DeductBalanceAsync(playerId, amount, "Blackjack", $"Bet placed: ₹{amount}");

            var game = GetGame();
            game.StartNewRound(amount);

            // Update balance from wallet
            var newBalance = await _walletService.GetBalanceAsync(playerId);
            game.PlayerBalance = (int)newBalance;

            SaveGame(game);

            return Json(new
            {
                playerHand = game.PlayerHand,
                dealerHand = new[] { game.DealerHand[0], "??" },
                playerScore = game.CalculateScore(game.PlayerHand),
                balance = game.PlayerBalance
            });
        }

        // AJAX: player hits
        [HttpPost]
        public IActionResult Hit()
        {
            var game = GetGame();
            game.PlayerHit();
            SaveGame(game);

            return Json(new
            {
                playerHand = game.PlayerHand,
                playerScore = game.CalculateScore(game.PlayerHand),
                isBust = game.CalculateScore(game.PlayerHand) > 21
            });
        }

        // AJAX: player stands → dealer plays, game over
        [HttpPost]
        public async Task<IActionResult> Stand()
        {
            var playerId = GetPlayerId();
            var game = GetGame();

            game.DealerPlay();
            var result = game.GetResult();

            // Calculate winnings and update wallet
            int playerScore = game.CalculateScore(game.PlayerHand);
            int dealerScore = game.CalculateScore(game.DealerHand);

            if (playerScore <= 21)
            {
                if (dealerScore > 21 || playerScore > dealerScore)
                {
                    // Player wins - add winnings to wallet
                    await _walletService.AddBalanceAsync(playerId, game.CurrentBet * 2, "Blackjack", $"Won: ₹{game.CurrentBet * 2}");
                }
                else if (playerScore == dealerScore)
                {
                    // Push - return bet to wallet
                    await _walletService.AddBalanceAsync(playerId, game.CurrentBet, "Blackjack", "Push - Bet returned");
                }
                // Loss - bet already deducted, do nothing
            }

            // Update balance from wallet
            var newBalance = await _walletService.GetBalanceAsync(playerId);
            game.PlayerBalance = (int)newBalance;

            SaveGame(game);

            return Json(new
            {
                dealerHand = game.DealerHand,
                dealerScore = game.CalculateScore(game.DealerHand),
                result,
                balance = game.PlayerBalance
            });
        }
    }
}