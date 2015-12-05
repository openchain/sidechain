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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Openchain.BitcoinGateway
{
    public class OpenchainClient
    {
        private readonly NBitcoin.Key openChainKey;
        private readonly Uri openChainUri;
        private readonly string assetName;
        private readonly Network network;

        public OpenchainClient(NBitcoin.Key openChainKey, string assetName, Uri openChainUri, Network network)
        {
            this.openChainKey = openChainKey;
            this.assetName = assetName;
            this.openChainUri = openChainUri;
            this.network = network;
        }

        public async Task AddAsset(InboundTransaction transaction)
        {
            byte[] serializedMutation = await CreateTransaction(transaction);
            if (serializedMutation == null)
                return;

            await SignAndSubmit(serializedMutation);
        }

        private async Task SignAndSubmit(byte[] serializedMutation)
        {
            byte[] hash = MessageSerializer.ComputeHash(serializedMutation);

            byte[] signature = openChainKey.Sign(new NBitcoin.uint256(hash)).ToDER();

            HttpClient client = new HttpClient();
            JObject json = JObject.FromObject(new
            {
                mutation = new ByteString(serializedMutation).ToString(),
                signatures = new[]
                {
                    new
                    {
                        pub_key = new ByteString(openChainKey.PubKey.ToBytes()).ToString(),
                        signature = new ByteString(signature).ToString()
                    }
                }
            });

            StringContent content = new StringContent(json.ToString());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            HttpResponseMessage response = await client.PostAsync(new Uri(openChainUri, "submit"), content);
            response.EnsureSuccessStatusCode();
        }

        public async Task MoveToRedemption(IList<OutboundTransaction> transactions, ByteString btcTransaction)
        {
            List<Record> records = new List<Record>();

            foreach (OutboundTransaction transaction in transactions)
            {
                // TODO: Allow double spending
                records.Add(new Record(
                    key: Encode($"/asset/{assetName}/tx/:DATA:{transaction.Version.ToString()}"),
                    value: Encode(JObject.FromObject(new { transactions = new[] { btcTransaction.ToString() } }).ToString()),
                    version: ByteString.Empty));

                records.Add(new Record(
                    key: transaction.RecordKey,
                    value: ByteString.Empty,
                    version: transaction.Version));
            }

            HttpClient client = new HttpClient();
            ByteString finalKey = Encode($"/asset/{assetName}/final/:ACC:/asset/{assetName}/");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"record?key={finalKey.ToString()}"));
            string stringResponse = await getValueResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync();
            ByteString finalVersion = ByteString.Parse((string)JObject.Parse(stringResponse)["version"]);
            long currentValue = ParseInt(ByteString.Parse((string)JObject.Parse(stringResponse)["value"]));

            records.Add(new Record(
                key: finalKey,
                value: Encode(currentValue + transactions.Sum(transaction => transaction.Amount)),
                version: finalVersion));

            Mutation mutation = new Mutation(Encode(this.openChainUri.ToString()), records, ByteString.Empty);

            byte[] serializedMutation = MessageSerializer.SerializeMutation(mutation);

            await SignAndSubmit(serializedMutation);
        }

        private async Task<byte[]> CreateTransaction(InboundTransaction transaction)
        {
            List<Record> records = new List<Record>();

            string issuanceAccount = $"/asset/{assetName}/in/{transaction.TransactionHash}/{transaction.OutputIndex}/";
            string asset = $"/asset/{assetName}/";
            string toAddress = transaction.Address;

            HttpClient client = new HttpClient();
            ByteString issuanceKey = Encode($"{issuanceAccount}:ACC:{asset}");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"record?key={issuanceKey.ToString()}"));
            string stringResponse = await getValueResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync();
            ByteString issuanceVersion = ByteString.Parse((string)JObject.Parse(stringResponse)["version"]);

            // The transaction has already been submitted
            if (issuanceVersion.Value.Count != 0)
                return null;

            ByteString key = Encode($"{toAddress}:ACC:{asset}");
            getValueResponse = await client.GetAsync(new Uri(openChainUri, $"record?key={key.ToString()}"));
            JObject toAccount = JObject.Parse(await getValueResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            ByteString version = ByteString.Parse((string)toAccount["version"]);
            long currentToBalance = ParseInt(ByteString.Parse((string)toAccount["value"]));

            records.Add(new Record(
                key: issuanceKey,
                value: new ByteString(BitConverter.GetBytes(-transaction.Amount).Reverse().ToArray()),
                version: ByteString.Empty));

            records.Add(new Record(
                key: key,
                value: new ByteString(BitConverter.GetBytes(currentToBalance + transaction.Amount).Reverse().ToArray()),
                version: version));

            Mutation mutation = new Mutation(
                Encode(this.openChainUri.ToString()),
                records,
                ByteString.Empty);

            return MessageSerializer.SerializeMutation(mutation);
        }

        public async Task<IList<OutboundTransaction>> GetUnprocessedTransactions()
        {
            string account = $"/asset/{assetName}/out/";
            string asset = $"/asset/{assetName}/";

            HttpClient client = new HttpClient();
            HttpResponseMessage accountsResponse = await client.GetAsync(new Uri(openChainUri, $"query/subaccounts?account={account}"));

            JArray records = JArray.Parse(await accountsResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync());

            List<OutboundTransaction> result = new List<OutboundTransaction>();

            foreach (JObject record in records)
            {
                ByteString mutationHash = ByteString.Parse((string)record["version"]);
                HttpResponseMessage transactionResponse = await client.GetAsync(new Uri(openChainUri, $"query/transaction?mutation_hash={mutationHash}"));

                JObject rawTransaction = JObject.Parse(await transactionResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync());

                Transaction transaction = MessageSerializer.DeserializeTransaction(ByteString.Parse((string)rawTransaction["raw"]));
                Mutation mutation = MessageSerializer.DeserializeMutation(transaction.Mutation);

                // TODO: Validate that the record mutation has an empty version

                string target = GetPayingAddress(mutation);
                if (target != null)
                {
                    long value = ParseInt(ByteString.Parse((string)record["value"]));

                    result.Add(new OutboundTransaction(ByteString.Parse((string)record["key"]), value, mutationHash, target));
                }
            }

            return result;
        }

        private string GetPayingAddress(Mutation outgoingTransaction)
        {
            try
            {
                string metadata = Encoding.UTF8.GetString(outgoingTransaction.Metadata.ToByteArray(), 0, outgoingTransaction.Metadata.Value.Count);

                JObject root = JObject.Parse(metadata);
                return BitcoinAddress.Create((string)root["routing"], network).ToString();
            }
            catch (JsonReaderException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static long ParseInt(ByteString value)
        {
            if (value.Value.Count == 0)
                return 0;
            else
                return BitConverter.ToInt64(value.Value.Reverse().ToArray(), 0);
        }

        private static ByteString Encode(string data)
        {
            return new ByteString(Encoding.UTF8.GetBytes(data));
        }

        private static ByteString Encode(long data)
        {
            return new ByteString(BitConverter.GetBytes(data).Reverse());
        }

        private class TransactionCache
        {
            private readonly Uri openChainUri;
            private readonly string assetName;

            public TransactionCache(Uri openChainUri, string assetName)
            {
                this.openChainUri = openChainUri;
                this.assetName = assetName;
            }

            public Dictionary<ByteString, Mutation> Transactions { get; } = new Dictionary<ByteString, Mutation>();

            public async Task<Mutation> GetMutation(ByteString hash)
            {
                Mutation mutation;
                if (!Transactions.TryGetValue(hash, out mutation))
                {
                    string account = $"/asset/{assetName}/out/";
                    string asset = $"/asset/{assetName}/";

                    HttpClient client = new HttpClient();

                    HttpResponseMessage getTransactionResponse = await client.GetAsync(new Uri(openChainUri, $"query/transaction?mutation_hash={hash.ToString()}"));
                    JToken rawTransaction = JToken.Parse(await getTransactionResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync());

                    Transaction transaction = MessageSerializer.DeserializeTransaction(ByteString.Parse((string)rawTransaction["raw"]));
                    mutation = MessageSerializer.DeserializeMutation(transaction.Mutation);

                    Transactions.Add(hash, mutation);
                }

                return mutation;
            }
        }
    }
}
