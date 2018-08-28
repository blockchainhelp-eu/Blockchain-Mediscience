using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace csmon.Models.Services
{
    public class Point
    {
        public DateTime X;
        public int Y;
    }

    public class TpsInfo
    {
        public Point[] Points;
    }

    public interface IIndexService
    {
        TpsInfo GetTpsInfo(string network);
        IndexData GetIndexData(string network);
        StatData GetStatData(string network);
        List<PoolInfo> GetPools(string network, int offset, int limit);
    }

    // Collects Tps points, blocks information
    public class IndexService : IIndexService, IHostedService, IDisposable
    {
        private class IndexServiceState
        {
            public Network Net;
            public Timer TimerForData;
            public Timer TimerForCache;
            public readonly ConcurrentQueue<Point> Points = new ConcurrentQueue<Point>();
            public readonly object PoolsLock = new object();
            public volatile List<PoolInfo> PoolsIn = new List<PoolInfo>();
            public volatile List<PoolInfo> PoolsOut = new List<PoolInfo>();        
            public int StatRequestCounter;
            public volatile StatData StatData = new StatData();
            public volatile IndexData IndexData = new IndexData();
            public bool EmulStop;
            public int EmulCounter;
        }

        private readonly ILogger _logger;
        private const int Period = 1000;
        private const int SizeIn = 300;
        private const int SizeOut = 100;
        public const int SizeOutAll = 100000;
        private const int TpsInterval = 10;
        private readonly Dictionary<string, IndexServiceState> _states = new Dictionary<string, IndexServiceState>();

        public IndexService(ILogger<IndexService> logger)
        {
            foreach (var network in Network.Networks)
            {
                var state = new IndexServiceState() { Net = network };
                _states.Add(network.Id, state);
                state.TimerForCache = new Timer(OnCacheTimer, state, Timeout.Infinite, 0);
                state.TimerForData = new Timer(OnDataTimer, state, Timeout.Infinite, 0);
            }
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var state in _states.Values)
            {
                state.TimerForCache.Change(Period, 0);
                state.TimerForData.Change(Period, 0);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var state in _states.Values)
            {
                state.TimerForCache.Change(Timeout.Infinite, 0);
                state.TimerForData.Change(Timeout.Infinite, 0);
            }
            return Task.CompletedTask;
        }

        public TpsInfo GetTpsInfo(string network)
        {
            var state = _states[network];
            return new TpsInfo { Points = state.Points.ToArray() };
        }

        public StatData GetStatData(string network)
        {
            return _states[network].StatData;
        }

        public IndexData GetIndexData(string network)
        {
            return _states[network].IndexData;
        }

        public List<PoolInfo> GetPools(string network, int offset, int limit)
        {
            return _states[network].PoolsOut.Skip(offset).Take(limit).ToList();
        }

        // ReSharper disable once UnusedMember.Local
        private static void OnCacheTimerEmul(object state)
        {
            var tpState = (IndexServiceState)state;
            var rnd = new Random();
            if (!tpState.PoolsOut.Any() && !tpState.PoolsIn.Any())
            {
                var np = new List<PoolInfo>();
                for (var i = 0; i < SizeOut; i++)
                    np.Add(new PoolInfo
                    {
                        Number = 2021952 + i,
                        Hash = "440B53FCC80578B478015A1C76F3A748758AE987203B4DDA96B569B3ADDA2859",
                        Time = DateTime.Now,
                        TxCount = rnd.Next(5, 15)
                    });
                np.Reverse();
                tpState.PoolsOut = np;
            }
            else
            {
                if (tpState.EmulStop)
                {
                    if (tpState.EmulCounter >= 10)
                    {
                        tpState.EmulCounter = 0;
                        tpState.EmulStop = false;
                    }
                    else if(!tpState.PoolsIn.Any())
                        tpState.EmulCounter++;
                }
                else
                    lock (tpState.PoolsLock)
                    {
                        var firstPoolNum = tpState.PoolsIn.Any()
                            ? tpState.PoolsIn[0].Number
                            : tpState.PoolsOut[0].Number;
                        var np = new List<PoolInfo>();
                        for (var i = 1; i < 2 + rnd.Next(0, 8); i++)
                            np.Add(new PoolInfo
                            {
                                Number = firstPoolNum + i,
                                Hash = "440B53FCC80578B478015A1C76F3A748758AE987203B4DDA96B569B3ADDA2859",
                                Time = DateTime.Now,
                                TxCount = rnd.Next(5, 15)
                            });
                        np.Reverse();
                        tpState.PoolsIn = np.TakeWhile(p => p.Number > firstPoolNum).Concat(tpState.PoolsIn).ToList();
                    }
            }
            tpState.TimerForCache.Change(Period, 0);
        }

        private void OnCacheTimer(object state)
        {
            var tpState = (IndexServiceState)state;
            try
            {
                if(tpState.Net.Api.EndsWith("/Api"))
                    using (var client = ApiFab.CreateNodeApi(tpState.Net.Ip))
                    {
                        // Service available
                        if(tpState.Net.Updating) tpState.Net.Updating = false;

                        // Request blocks
                        if ((!tpState.PoolsOut.Any() && !tpState.PoolsIn.Any()))
                        {
                            var result = client.PoolListGet(0, SizeOut);
                            tpState.PoolsOut = result.Pools.Select(p => new PoolInfo(p)).ToList();
                        }
                        else
                        {
                            var result = client.PoolListGet(0, 20);
                            lock (tpState.PoolsLock)
                            {
                                var firstPoolNum = tpState.PoolsIn.Any()
                                    ? tpState.PoolsIn[0].Number
                                    : tpState.PoolsOut[0].Number;
                                var nPools = result.Pools.TakeWhile(p => (p.PoolNumber > firstPoolNum) || (p.PoolNumber < firstPoolNum - 1000)).Select(p => new PoolInfo(p)).ToList();
                                tpState.PoolsIn = nPools.Concat(tpState.PoolsIn).ToList();
                            }
                        }

                        // Request stats
                        if (tpState.StatRequestCounter == 0)
                        {
                            var stats = client.StatsGet();
                            if (stats != null && stats.Stats.Count >= 4)
                            {
                                var statsSorted = stats.Stats.OrderBy(s => s.PeriodDuration).ToList();
                                var statData = new StatData();
                                for (var i = 0; i < 4; i++)
                                    statData.Pdata[i] = new PeriodData(statsSorted[i]);
                                // Smart contracts count = n
                                using (var db = ApiFab.GetDbContext())
                                    statData.Correct(db.Smarts.Count(s => s.Network == tpState.Net.Id));
                                tpState.StatData = statData;
                            }
                        }
                    }
                else 
                    using (var client = ApiFab.CreateTestApi(tpState.Net.Ip))
                    {
                        // Service available
                        if (tpState.Net.Updating) tpState.Net.Updating = false;

                        // Request blocks
                        if ((!tpState.PoolsOut.Any() && !tpState.PoolsIn.Any()))
                        {
                            var result = client.PoolListGet(0, SizeOut);
                            tpState.PoolsOut = result.Pools.Where(p => p.PoolNumber > 0).Select(p => new PoolInfo(p)).ToList();
                        }
                        else
                        {
                            var result = client.PoolListGet(0, 20);
                            lock (tpState.PoolsLock)
                            {
                                var firstPoolNum = tpState.PoolsIn.Any()
                                    ? tpState.PoolsIn[0].Number
                                    : tpState.PoolsOut[0].Number;
                                var nPools = result.Pools.Where(p => p.PoolNumber > 0).TakeWhile(p => (p.PoolNumber > firstPoolNum) || (p.PoolNumber < firstPoolNum - 1000)).Select(p => new PoolInfo(p)).ToList();
                                tpState.PoolsIn = nPools.Concat(tpState.PoolsIn).ToList();
                            }
                        }

                        // Request stats
                        if (tpState.StatRequestCounter == 0)
                        {
                            var stats = client.StatsGet();
                            if (stats != null && stats.Stats.Count >= 4)
                            {
                                var statsSorted = stats.Stats.OrderBy(s => s.PeriodDuration).ToList();
                                var statData = new StatData();
                                for (var i = 0; i < 4; i++)
                                    statData.Pdata[i] = new PeriodData(statsSorted[i]);
                                tpState.StatData = statData;
                            }
                        }
                    }
                if (tpState.StatRequestCounter < (120000 / Period))
                    tpState.StatRequestCounter++;
                else
                    tpState.StatRequestCounter = 0;
            }
            catch (Thrift.Transport.TTransportException e)
            {
                tpState.Net.Updating = true;
                _logger.LogError(e, "TTransportException in TpsSource.OnCacheTimer");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception in TpsSource.OnCacheTimer");
            }
            tpState.TimerForCache.Change(Period, 0);
        }

        private void OnDataTimer(object state)
        {
            var tpState = (IndexServiceState) state;
            var curTime = DateTime.Now;
            try
            {                
                // Take data from cache
                lock (tpState.PoolsLock)
                {
                    int inCount = tpState.PoolsIn.Count, addNum;
                    if (tpState.Net.CachePools)
                    {
                        var bps = inCount < 20 ? 1 :
                                 (inCount) / (curTime - tpState.PoolsIn.Last().Time).TotalSeconds;
                        var bppInt = (int) Math.Floor(bps * Period / 1000);
                        addNum = inCount > bppInt ? bppInt : inCount;
                        if (inCount < SizeIn*0.75) addNum -= 1;
                        else if (inCount > SizeIn) addNum += 1;
                        if (addNum < 1 && inCount > 0) addNum = 1;
                    }
                    else
                        addNum = inCount;

                    if (addNum > 0)
                    {
                        // Get pools to add
                        var addPools = tpState.PoolsIn.TakeLast(addNum).ToList();
                        tpState.PoolsIn.RemoveRange(inCount - addNum, addNum);
                        // Correct time
                        foreach (var pool in addPools)
                            pool.Time = curTime;
                        // Add pools
                        tpState.PoolsOut = addPools.Concat(tpState.PoolsOut.Take(SizeOutAll - addNum)).ToList();                            
                    }
                    Debug.Print($"net: {tpState.Net.Id} addNum={addNum} InCount={tpState.PoolsIn.Count} OutCount={tpState.PoolsOut.Count}\n");
                }

                // Convert                
                var lastPoolInfos = tpState.PoolsOut.Take(SizeOut).ToList();

                // Calculate TPS point
                if ((int)(curTime - curTime.Date).TotalSeconds % TpsInterval == 0)
                {
                    while (tpState.Points.Count >= 100) tpState.Points.TryDequeue(out _);
                    var txCount = lastPoolInfos.Where(p => p.Time > curTime.AddSeconds(-TpsInterval)).Sum(p => p.TxCount);
                    tpState.Points.Enqueue(new Point { X = curTime, Y = txCount / TpsInterval });
                }

                // Prepare data for main page
                var indexData = new IndexData
                {
                    LastBlocks = lastPoolInfos,
                    LastBlockData = {Now = curTime}
                };
                if (lastPoolInfos.Any())
                {
                    var lastPool = lastPoolInfos.First();
                    indexData.LastBlockData.LastBlock = lastPool.Number;
                    indexData.LastBlockData.LastTime = lastPool.Time;
                    indexData.LastBlockData.LastBlockTxCount = lastPool.TxCount;
                }
                
                // Save
                tpState.IndexData = indexData;                
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception in TpsSource.OnDataTimer");
            }
            tpState.TimerForData.Change(Period, 0);
        }

        public void Dispose()
        {
            foreach (var state in _states.Values)
            {
                state.TimerForCache?.Dispose();
                state.TimerForData?.Dispose();
            }
        }
    }
}
