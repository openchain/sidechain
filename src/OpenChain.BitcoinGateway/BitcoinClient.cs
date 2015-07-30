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
        private readonly Key receivingKey;
        private readonly Key storageKey;
        private readonly Network network;

        public BitcoinClient(Uri url, Key receivingKey, Key storageKey, Network network)
        {
            this.url = url;
            this.receivingKey = receivingKey;
            this.storageKey = storageKey;
            this.network = network;
        }

        public async Task<List<InboundTransaction>> GetUnspentOutputs()
        {
            HttpClient client = new HttpClient();
            BitcoinAddress address = receivingKey.ScriptPubKey.GetDestinationAddress(this.network);
            HttpResponseMessage response = await client.GetAsync(new Uri(url, $"addresses/{address.ToString()}/unspents"));

            string body = await response.Content.ReadAsStringAsync();

            JArray outputs = JArray.Parse(body);

            List<InboundTransaction> result = new List<InboundTransaction>();
            foreach (JObject output in outputs)
            {
                string transactionHash = (string)output["transaction_hash"];
                int outputIndex = (int)output["output_index"];
                long amount = (long)output["value"];

                HttpResponseMessage transactionResponse = await client.GetAsync(new Uri(url, $"transactions/{transactionHash}"));
                JObject transaction = JObject.Parse(await transactionResponse.Content.ReadAsStringAsync());
                string inboundAddress = (string)transaction["inputs"][0]["addresses"][0];

                result.Add(new InboundTransaction(transactionHash, outputIndex, null, amount, inboundAddress));
            }

            return result;
        }
    }
}
