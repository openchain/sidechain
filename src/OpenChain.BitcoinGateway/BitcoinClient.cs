using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace OpenChain.BitcoinGateway
{
    public class BitcoinClient
    {
        private readonly Uri url;
        private readonly BitcoinExtKey receivingKey;
        private readonly BitcoinExtKey storageKey;

        public BitcoinClient(Uri url, BitcoinExtKey receivingKey, BitcoinExtKey storageKey)
        {
            this.url = url;
            this.receivingKey = receivingKey;
            this.storageKey = storageKey;
        }

        public async Task<List<InboundTransaction>> GetUnspentOutputs()
        {
            HttpClient client = new HttpClient();
            BitcoinAddress address = receivingKey.ScriptPubKey.GetDestinationAddress(receivingKey.Network);
            HttpResponseMessage response = await client.GetAsync(new Uri(url, $"addresses/{address.ToString()}/unspents"));

            string body = await response.Content.ReadAsStringAsync();

            JArray outputs = JArray.Parse(body);

            List<InboundTransaction> result = new List<InboundTransaction>();
            foreach (JObject output in outputs)
            {
                string transactionHash = (string)output["transaction_hash"];
                long amount = (long)output["value"];

                result.Add(new InboundTransaction(transactionHash, null, amount));
            }

            return result;
        }
    }
}
