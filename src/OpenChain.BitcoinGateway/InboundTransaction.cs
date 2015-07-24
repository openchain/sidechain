using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenChain.BitcoinGateway
{
    public class InboundTransaction
    {
        public InboundTransaction(string transactionHash, string asset, long amount)
        {
            this.TransactionHash = transactionHash;
            this.Asset = asset;
            this.Amount = amount;
        }

        public string TransactionHash { get; }

        public string Asset { get; }

        public long Amount { get; }
    }
}
