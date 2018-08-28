using System.Linq;
using System.Net;
using System.Threading;
using csmon.Models;
using csmon.Models.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace csmon.Controllers
{
    public class MonitorController : Controller
    {
        private readonly IIndexService _indexService;

        public MonitorController(IConfiguration configuration, IIndexService indexService)
        {
            _indexService = indexService;
        }

        public IActionResult Index()
        {
            ViewData["stats"] = _indexService.GetStatData(RouteData.Values["network"].ToString());
            return View(new IndexData());
        }

        [Route("{network}/[controller]/[action]/{id}/{page:int=1}")]
        public IActionResult Ledger(string id, int page=1)
        {
            ViewData["blockId"] = id;
            ViewData["page"] = page;
            return View(new TransactionsData());
        }

        public IActionResult Account(string id)
        {
            ViewData["accId"] = id;
            ViewData["accIdEnc"] = WebUtility.UrlEncode(id);
            return View();
        }

        public IActionResult Transaction(string id)
        {
            ViewData["id"] = id;
            return View(new TransactionInfo());
        }

        public IActionResult Ledgers(int id = 1)
        {
            ViewData["page"] = id;
            return View();
        }

        [HttpPost]
        public IActionResult Search(string query)
        {
            var network = RouteData.Values["network"].ToString();            
            if (string.IsNullOrEmpty(query))
                return Redirect(Request.Headers["Referer"].ToString());

            if (query.Contains("."))
                return RedirectToAction(nameof(Transaction), new {id = query, netwok = network});

            if (Network.GetById(network).Api.EndsWith("/Api"))
            {
                if (query.StartsWith("CS"))
                    return Redirect($"/{network}/{nameof(Monitor)}/{nameof(Contract)}/{query}");

                if (query.All("0123456789ABCDEF".Contains))
                    return Redirect($"/{network}/{nameof(Monitor)}/{nameof(Ledger)}/{query}");

                if (query.All("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".Contains))
                    return Redirect($"/{network}/{nameof(Monitor)}/{nameof(Account)}/{query}");
            }
            else
            {
                if (query.All("0123456789ABCDEF".Contains))
                    return Redirect($"/{network}/{nameof(Monitor)}/{nameof(Ledger)}/{query}");

                if (query.All("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".Contains))
                    return Redirect($"/{network}/{nameof(Monitor)}/{nameof(Contract)}/{query}");
            }

            ViewData["id"] = query;
            return View("NotFound");
        }

        public IActionResult Error()
        {
            return View();
        }

        public IActionResult NotFound(string id)
        {
            ViewData["id"] = id;
            return View();
        }

        [HttpPost]
        public IActionResult SelectNetwork(string network)
        {
            return RedirectToAction(nameof(Index), new {network});
        }

        public IActionResult Contracts(int id = 1)
        {
            ViewData["page"] = id;
            return View();
        }

        public IActionResult Contract(string id)
        {
            ViewData["id"] = id;
            return View(new ContractInfo());
        }
    }
}
