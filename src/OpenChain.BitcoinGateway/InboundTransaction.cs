using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenChain.BitcoinGateway
{
    public class InboundTransaction
    {
        public InboundTransaction(string transactionHash, int outputIndex, string asset, long amount, string address)
        {
            this.TransactionHash = transactionHash;
            this.OutputIndex = outputIndex; 
            this.Asset = asset;
            this.Amount = amount;
            this.Address = address;
        }

        public string TransactionHash { get; }

        public int OutputIndex { get; }

        public string Asset { get; }

        public long Amount { get; }

        public string Address { get; }
    }
}
