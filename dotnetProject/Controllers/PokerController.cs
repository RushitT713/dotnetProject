using Microsoft.AspNetCore.Mvc;

namespace dotnetProject.Controllers
{
    public class PokerController : Controller
    {
        [HttpGet]
        public IActionResult Index(string lobbyCode, string playerName)
        {
            ViewBag.LobbyCode = lobbyCode;
            ViewBag.PlayerName = playerName;
            return View();
        }
    }
}