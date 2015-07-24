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
        private readonly BitcoinExtKey key;
        private readonly BitcoinClient bitcoinClient;

        public PegGateway(BitcoinExtKey key, BitcoinClient bitcoinClient)
        {
            this.key = key;
            this.bitcoinClient = bitcoinClient;    
        }

        public async Task GetBitcoinTransactions()
        {
            while (true)
            {
                await 
            }
        }
    }
}
