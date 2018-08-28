using System;
using System.Collections.Generic;
using System.Linq;
using csmon.Models;
using csmon.Models.Db;
using csmon.Models.Services;
using Microsoft.AspNetCore.Mvc;
using TestApi;

namespace csmon.Controllers
{
    public class TestApiController : Controller
    {
        private readonly IIndexService _indexService;
        private readonly INodesService _nodesService;
        private readonly IGraphService _graphService;
        private string Net => RouteData.Values["network"].ToString();

        // ReSharper disable once EmptyConstructor
        public TestApiController(IIndexService indexService, INodesService nodesService, IGraphService graphService)
        {
            _indexService = indexService;
            _nodesService = nodesService;
            _graphService = graphService;
        }

        private API.Client CreateApi()
        {
            return ApiFab.CreateTestApi(Network.GetById(Net).Ip);
        }

        public IndexData IndexData(int id)
        {
            var indexData = _indexService.GetIndexData(Net);
            return new IndexData
            {
                LastBlockData = indexData.LastBlockData,
                LastBlocks = indexData.LastBlocks.TakeWhile(b => b.Number > id).ToList()
            };
        }

        public LedgersData Ledgers(int id)
        {
            const int limit = 100;
            if (id <= 0) id = 1;
            var ledgers = _indexService.GetPools(Net, (id - 1) * limit, limit);
            var lastPage = ConvUtils.GetNumPages(IndexService.SizeOutAll, limit);
            var result = new LedgersData
            {
                Page = id,
                Ledgers = ledgers,
                HaveNextPage = id < lastPage,
                LastPage = lastPage,
                NumStr = ledgers.Any() ? $"{ledgers.Last().Number} - {ledgers.First().Number}" : "-"
            };
            return result;
        }

        public TransactionsData PoolData(string id)
        {
            using (var client = CreateApi())
            {
                var poolHash = ConvUtils.ConvertHashBack(id);
                var pool = client.PoolInfoGet(poolHash, 0);

                var result = new TransactionsData
                {
                    Page = 1,
                    Found = pool.IsFound,
                    Info = new PoolInfo(pool.Pool)
                };
                return result;
            }
        }

        public TransactionsData PoolTransactions(string hash, int page, int txcount)
        {
            const int numPerPage = 50;
            if (page <= 0) page = 1;
            using (var client = CreateApi())
            {
                var lastPage = ConvUtils.GetNumPages(txcount, numPerPage);
                if (page > lastPage) page = lastPage;
                var result = new TransactionsData
                {
                    Page = page,
                    LastPage = lastPage,
                    HaveNextPage = page < lastPage
                };
                var offset = numPerPage * (page - 1);
                var poolTr = client.PoolTransactionsGet(ConvUtils.ConvertHashBack(hash), offset, numPerPage);
                var i = offset + 1;
                foreach (var t in poolTr.Transactions)
                {
                    var tInfo = new TransactionInfo(i, t.Id, t.Trxn);
                    result.Transactions.Add(tInfo);
                    i++;
                }
                result.NumStr = poolTr.Transactions.Any() ? $"{offset + 1} - {offset + poolTr.Transactions.Count} of {txcount}" : "0";
                return result;
            }
        }

        public string Balance(string id)
        {
            using (var client = CreateApi())
            {
                var balance = client.BalanceGet(Base58Encoding.Decode(id), "cs");
                return ConvUtils.FormatAmount(balance.Amount);
            }
        }

        public TransactionInfo TransactionInfo(string id)
        {
            using (var client = CreateApi())
            {
                var ids = id.Split('.');
                var trId = new TransactionId()
                {
                    Index = int.Parse(ids[1]) - 1,
                    PoolHash = ConvUtils.ConvertHashBack(ids[0])
                };
                var tr = client.TransactionGet(trId);
                var tInfo = new TransactionInfo(0, null, tr.Transaction.Trxn) { Id = id, Found = tr.Found };
                if (!tr.Found)
                    return tInfo;
                if (string.IsNullOrEmpty(tInfo.PoolHash)) return tInfo;
                var pool = client.PoolInfoGet(ConvUtils.ConvertHashBack(tInfo.PoolHash), 0);
                tInfo.Time = ConvUtils.UnixTimeStampToDateTime(pool.Pool.Time);
                return tInfo;
            }
        }
        
        public TransactionsData AccountTransactions(string id, int page, bool conv = true)
        {
            const int numPerPage = 15;
            if (page <= 0) page = 1;
            using (var client = CreateApi())
            {                
                
                var offset = numPerPage * (page - 1);
                var trs = client.TransactionsGet(Base58Encoding.Decode(id), offset, numPerPage + 1);
                var result = new TransactionsData
                {
                    Page = page,
                    Transactions = new List<TransactionInfo>(),
                    HaveNextPage = trs.Transactions.Count > numPerPage
                };
                var count = Math.Min(numPerPage, trs.Transactions.Count);
                for (var i = 0; i < count; i++)
                {
                    var t = trs.Transactions[i];
                    var tInfo = new TransactionInfo(i + offset + 1, t.Id, t.Trxn);
                    result.Transactions.Add(tInfo);
                }
                result.NumStr = count > 0 ? $"{offset + 1} - {offset + count}" : "-";
                return result;
            }
        }

        public TransactionsData ContractTransactions(string id, int page)
        {
            return AccountTransactions(id, page, false);
        }

        public DateTime GetTransactionTime(string id)
        {
            using (var client = CreateApi())
            {
                var poolHash = id.Split(".")[0];
                var pool = client.PoolInfoGet(ConvUtils.ConvertHashBack(poolHash), 0);
                return ConvUtils.UnixTimeStampToDateTime(pool.Pool.Time);
            }
        }

        public TokenAmounts AccountTokens(string id, string tokens)
        {
            using (var client = CreateApi())
            {
                var result = new TokenAmounts();
                if (id == null || tokens == null) return result;
                foreach (var token in tokens.Split(","))
                {
                    var balance = client.BalanceGet(Base58Encoding.Decode(id), token);
                    result.Tokens.Add(new TokenAmount {Token = token, Value = ConvUtils.FormatAmount(balance.Amount)});
                }
                return result;
            }
        }

        public ContractsData GetContracts(int page)
        {
            const int numPerPage = 20;
            if (page <= 0) page = 1;
            var result = new ContractsData { Page = page };
            using (var client = CreateApi())
            {
                var offset = numPerPage * (page - 1);
                var res = client.SmartContractsAllListGet(offset, numPerPage + 1);
                result.HaveNextPage = res.SmartContractsList.Count > numPerPage;
                var count = Math.Min(numPerPage, res.SmartContractsList.Count);
                for (var i = 0; i < count; i++)
                {
                    var c = res.SmartContractsList[i];
                    var cInfo = new ContractLinkInfo(i + offset + 1, Base58Encoding.Encode(c.Address));
                    result.Contracts.Add(cInfo);
                }
            }
            result.NumStr = result.Contracts.Any() ? $"{result.Contracts.First().Index} - {result.Contracts.Last().Index}" : "0";
            return result;
        }

        public ContractInfo ContractInfo(string id)
        {
            using (var client = CreateApi())
            {
                var res = client.SmartContractGet(Base58Encoding.Decode(id));
                return new ContractInfo(res.SmartContract) { Found = res.Status.Code == 0 };
            }
        }

        public TpsInfo GetTpsData()
        {
            return _indexService.GetTpsInfo(Net);
        }

        public NodesData GetNodesData()
        {
            return _nodesService.GetNodes(Net);
        }

        public Node GetNodeData(string id)
        {
            return _nodesService.FindNode(id);
        }

        public GraphData GetGraphData()
        {
            return _graphService.GetGraphData();
        }
    }
}
