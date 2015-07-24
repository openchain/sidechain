using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace OpenChain.BitcoinGateway
{
    public class OpenChainClient
    {
        private readonly BitcoinExtKey openChainKey;

        public OpenChainClient(BitcoinExtKey openChainKey)
        {
            this.openChainKey = openChainKey;
        }

        public async Task AddAsset(InboundTransaction transaction)
        {

        }
    }
}
