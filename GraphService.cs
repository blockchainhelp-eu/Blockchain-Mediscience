using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace csmon.Models.Services
{
    public class GraphNode
    {
        public int Size { get; set; }
        public string Type { get; set; }
    }

    public class GraphLink
    {
        public int Node1 { get; set; }
        public int Node2 { get; set; }
    }

    // Contains graph data
    public class GraphData
    {
        public List<GraphNode> Nodes = new List<GraphNode>();
        public List<GraphLink> Links = new List<GraphLink>();
    }


    public interface IGraphService
    {
        GraphData GetGraphData();
    }

    // Data generator for activity graph
    public class GraphService : IHostedService, IDisposable, IGraphService
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger _logger;
        private const int Period = 10000; // 10 seconds
        private Timer _timer;
        private readonly Random _rnd = new Random();

        // ReSharper disable once SuggestBaseTypeForParameter
        public GraphService(IServiceProvider provider, ILogger<GraphService> logger)
        {
            _provider = provider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(OnTimer, null, Period, 0);
            return Task.CompletedTask;
        }

        private void OnTimer(object state)
        {
            _timer.Change(Period, 0);
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

        public GraphData GetGraphData()
        {
            const int numAccounts = 100;
            var accounts = new SortedDictionary<int, int>();

            var result = new GraphData();

            for (var b = 0; b < 3; b++)
            {
                result.Nodes.Add(new GraphNode { Size = 15, Type = "block" });
                var blockIndex = result.Nodes.Count - 1;
                var c = _rnd.Next(30, 50);
                for (var i = 0; i < c; i++)
                {
                    result.Nodes.Add(new GraphNode { Size = _rnd.Next(2, 7), Type = "tx" });
                    var txIndex = result.Nodes.Count - 1;
                    result.Links.Add(new GraphLink { Node1 = blockIndex, Node2 = txIndex });

                    var acc1 = _rnd.Next(1, numAccounts);
                    if (!accounts.ContainsKey(acc1))
                    {
                        result.Nodes.Add(new GraphNode { Size = _rnd.Next(8, 10), Type = "account" });
                        accounts.Add(acc1, result.Nodes.Count - 1);
                    }
                    result.Links.Add(new GraphLink { Node1 = txIndex, Node2 = accounts[acc1] });

                    var acc2 = _rnd.Next(1, numAccounts);
                    if (!accounts.ContainsKey(acc2))
                    {
                        result.Nodes.Add(new GraphNode { Size = _rnd.Next(8, 10), Type = "account" });
                        accounts.Add(acc2, result.Nodes.Count - 1);
                    }
                    result.Links.Add(new GraphLink { Node1 = txIndex, Node2 = accounts[acc2] });
                }
            }
            
            return result;
        }
    }
}
