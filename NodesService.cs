using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using csmon.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace csmon.Models.Services
{
    // Contains list of nodes
    public class NodesData
    {
        public List<Node> Nodes = new List<Node>();
    }


    public interface INodesService
    {
        NodesData GetNodes(string network);
        Node FindNode(string id);
    }

    public class NodesService : IHostedService, IDisposable, INodesService
    {        
        private readonly ILogger _logger;
        public const int LiveTimeMinutes = 4;
        private const int Period = 1000*60*2;
        private Timer _timer;

        public NodesService(ILogger<NodesService> logger)
        {
            _logger = logger;
        }        

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(OnTimer, null, Period, 0);
            return Task.CompletedTask;
        }

        private void OnTimer(object state)
        {
            foreach (var network in Network.Networks)
            {
                for (var numOfTryes = 0; numOfTryes < 3; numOfTryes++)
                {
                    try
                    {
                        using (var client = ApiFab.CreateSignalApi(network.SignalIp))
                        {
                            var result = client.GetActiveNodes();
                            var nodes = result.Nodes.Distinct((n1, n2) => n1.Ip.Equals(n2.Ip)).ToArray();
                            using (var db = ApiFab.GetDbContext())
                            {
                                var dbNodes = db.Nodes.ToArray();
                                foreach (var serverNode in nodes)
                                {
                                    var ip = serverNode.Ip;
                                    if (string.IsNullOrEmpty(ip)) continue;
                                    var exnode = dbNodes.FirstOrDefault(n => n.Ip.Equals(ip));
                                    if (exnode != null)
                                    {
                                        exnode.ModifyTime = DateTime.Now;
                                        exnode.Version = serverNode.Version;
                                        db.Nodes.Update(exnode);
                                    }
                                    else
                                    {
                                        var node = new Node
                                        {
                                            Ip = ip,
                                            Version = serverNode.Version,
                                            Network = network.Id
                                        };
                                        db.Nodes.Add(node);
                                    }

                                    db.SaveChanges();
                                }
                            }
                        }
                        break;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"Exception in NodesSource NumOfTryes={numOfTryes}");
                        Thread.Sleep(1000);
                    }
                }
            }

            _timer.Change(Period, 0);
        }

        public static async Task<string> GetAsync(Uri uri)
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "java-ipapi-client");
            return await httpClient.GetStringAsync(uri);
        }

        public NodesData GetNodes(string network)
        {
            var net = Network.GetById(network);
            using (var db = ApiFab.GetDbContext())
            {
                var result = new NodesData
                {
                    Nodes = db.Nodes.Where(n => (n.Network == network) &&
                            (net.RandomNodes || (n.ModifyTime.AddMinutes(LiveTimeMinutes) >= DateTime.Now)))
                        .OrderBy(n => n.ModifyTime)
                        .Take(1000).ToList()
                };
                return result;
            }
        }

        public Node FindNode(string id)
        {
            using (var db = ApiFab.GetDbContext())
            {
                return db.Nodes.Find(id);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
