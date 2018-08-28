using csmon.Models.Services;
using Microsoft.AspNetCore.Mvc;

namespace csmon.Controllers
{
    public class ToolsController : Controller
    {
        // ReSharper disable once EmptyConstructor
        public ToolsController()
        {
        }

        public IActionResult Tps()
        {
            return View();
        }

        public IActionResult Nodes()
        {            
            return View(new NodesData());
        }

        public IActionResult Node(string id)
        {
            ViewData["id"] = id;
            return View();
        }

        public IActionResult ActivityGraph()
        {
            return View(new GraphData());
        }

    }
}
