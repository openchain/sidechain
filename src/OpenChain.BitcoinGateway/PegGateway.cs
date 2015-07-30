using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Framework.DependencyInjection;
using NBitcoin;

namespace OpenChain.BitcoinGateway
{
    public class PegGateway
    {
        private readonly BitcoinClient bitcoinClient;
        private readonly OpenChainClient openChainClient;

        public PegGateway(BitcoinClient bitcoinClient, OpenChainClient openChainClient)
        {
            this.bitcoinClient = bitcoinClient;
            this.openChainClient = openChainClient;
        }

        public async Task GetBitcoinTransactions()
        {
            while (true)
            {
                IList<InboundTransaction> transactions = await this.bitcoinClient.GetUnspentOutputs();

                foreach (InboundTransaction transaction in transactions)
                {
                    await openChainClient.AddAsset(transaction);
                }
            }
        }
    }
}
