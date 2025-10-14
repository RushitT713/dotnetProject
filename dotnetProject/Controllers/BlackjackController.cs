using dotnetProject.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
namespace YourProjectNamespace.Controllers
{
    public class BlackjackController : Controller
    {
        private const string SessionKey = "BlackjackGame";

        private BlackjackGame GetGame()
        {
            var json = HttpContext.Session.GetString(SessionKey);
            BlackjackGame game = json == null
                ? new BlackjackGame()
                : JsonConvert.DeserializeObject<BlackjackGame>(json);

            // always save back so Session is initialized
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
        public IActionResult Index()
        {
            var game = GetGame();
            return View(game);
        }

        // AJAX: place your bet and deal initial cards
        [HttpPost]
        public IActionResult PlaceBet([FromForm] int amount)
        {
            var game = GetGame();
            game.StartNewRound(amount);
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
        public IActionResult Stand()
        {
            var game = GetGame();
            game.DealerPlay();
            var result = game.GetResult();
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
