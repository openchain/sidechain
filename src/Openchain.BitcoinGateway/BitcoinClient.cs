// Copyright 2015 Coinprism, Inc.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace Openchain.BitcoinGateway
{
    public class BitcoinClient
    {
        private readonly Uri url;
        private readonly Key storageKey;
        private readonly ILogger logger;
        private readonly long defaultFees = 5000;

        public BitcoinClient(Uri url, Key receivingKey, Key storageKey, Network network, ILogger logger)
        {
            this.url = url;
            this.ReceivingKey = receivingKey;
            this.storageKey = storageKey;
            this.logger = logger;
            this.Network = network;
            this.ReceivingAddress = ReceivingKey.PubKey.GetAddress(Network).ToString();
        }

        public Key ReceivingKey { get; }

        public string ReceivingAddress { get; }

        public Network Network { get; }

        public async Task<List<InboundTransaction>> GetUnspentOutputs()
        {
            HttpClient client = new HttpClient();
            BitcoinAddress address = ReceivingKey.ScriptPubKey.GetDestinationAddress(this.Network);
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
                string target = (string)FindDestination(transaction);

                if (target != null)
                    result.Add(new InboundTransaction(transactionHash, outputIndex, null, amount, target));
            }

            return result;
        }

        private string FindDestination(JObject transaction)
        {
            foreach (JObject output in transaction["outputs"])
            {
                ByteString script = ByteString.Parse((string)output["script"]);
                Script parsedScript = new Script(script.ToByteArray());

                foreach (Op opCode in parsedScript.ToOps())
                {
                    if (opCode.PushData != null && opCode.PushData.Length >= 2 && !opCode.IsInvalid)
                    {
                        if (opCode.PushData[0] == 'O' && opCode.PushData[1] == 'G')
                            return Encoding.UTF8.GetString(opCode.PushData, 2, opCode.PushData.Length - 2);
                    }
                }
            }

            return null;
        }

        public async Task<ByteString> IssueWithdrawal(IList<OutboundTransaction> transactions)
        {
            HttpClient client = new HttpClient();
            BitcoinAddress address = storageKey.ScriptPubKey.GetDestinationAddress(this.Network);
            HttpResponseMessage response = await client.GetAsync(new Uri(url, $"addresses/{address.ToString()}/unspents"));

            string body = await response.Content.ReadAsStringAsync();

            JArray outputs = JArray.Parse(body);

            TransactionBuilder builder = new TransactionBuilder();
            builder.AddKeys(storageKey.GetBitcoinSecret(Network));
            foreach (JObject output in outputs)
            {
                string transactionHash = (string)output["transaction_hash"];
                uint outputIndex = (uint)output["output_index"];
                long amount = (long)output["value"];

                builder.AddCoins(new Coin(uint256.Parse(transactionHash), outputIndex, new Money(amount), storageKey.ScriptPubKey));
            }

            foreach (OutboundTransaction outboundTransaction in transactions)
            {
                builder.Send(BitcoinAddress.Create(outboundTransaction.Target, Network).ScriptPubKey, new Money(outboundTransaction.Amount));
            }

            builder.SendFees(defaultFees);
            builder.SetChange(storageKey.ScriptPubKey, ChangeType.All);

            NBitcoin.Transaction transaction = builder.BuildTransaction(true);

            return new ByteString(transaction.ToBytes());
        }

        public async Task<string> MoveToStorage(InboundTransaction transaction)
        {
            NBitcoin.Transaction tx = new TransactionBuilder()
                .AddKeys(ReceivingKey)
                .AddCoins(new Coin(
                    uint256.Parse(transaction.TransactionHash),
                    (uint)transaction.OutputIndex,
                    transaction.Amount,
                    ReceivingKey.ScriptPubKey))
                .Send(storageKey.ScriptPubKey, transaction.Amount - defaultFees)
                .SendFees(defaultFees)
                .BuildTransaction(true);

            string result = await SubmitTransaction(new ByteString(tx.ToBytes()));

            return result;
        }

        public async Task<string> SubmitTransaction(ByteString transaction)
        {
            HttpClient client = new HttpClient();
            StringContent content = new StringContent($"\"{transaction.ToString()}\"");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            HttpResponseMessage response = await client.PostAsync(new Uri(url, $"sendrawtransaction"), content);
            response.EnsureSuccessStatusCode();

            JToken result = JToken.Parse(await response.Content.ReadAsStringAsync());
            return (string)result;
        }
    }
}
