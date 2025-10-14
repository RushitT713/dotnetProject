using Microsoft.AspNetCore.Mvc;

namespace dotnetProject.Controllers
{
    public class LobbyController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}
