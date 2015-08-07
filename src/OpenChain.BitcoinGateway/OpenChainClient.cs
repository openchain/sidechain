using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenChain;

namespace OpenChain.BitcoinGateway
{
    public class OpenChainClient
    {
        private readonly NBitcoin.Key openChainKey;
        private readonly Uri openChainUri;
        private readonly string assetName;
        private readonly Regex accountRegex = new Regex("/p2pkh/(?<address>[a-zA-Z0-9]+)(/.*$)?", RegexOptions.Compiled);

        public OpenChainClient(NBitcoin.Key openChainKey, string assetName, Uri openChainUri)
        {
            this.openChainKey = openChainKey;
            this.assetName = assetName;
            this.openChainUri = openChainUri;
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
                transaction = new BinaryData(serializedMutation).ToString(),
                signatures = new[]
                {
                    new
                    {
                        pub_key = new BinaryData(openChainKey.PubKey.ToBytes()).ToString(),
                        signature = new BinaryData(signature).ToString()
                    }
                }
            });

            StringContent content = new StringContent(json.ToString());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            HttpResponseMessage response = await client.PostAsync(new Uri(openChainUri, "submit"), content);
            response.EnsureSuccessStatusCode();
        }

        public async Task MoveToRedemption(IList<OutboundTransaction> transactions, BinaryData btcTransaction)
        {
            List<Record> records = new List<Record>();
            
            foreach (OutboundTransaction transaction in transactions)
            {
                // TODO: Allow double spending
                records.Add(new Record(
                    key: Encode($"/asset/{assetName}/processed/{transaction.MutationHash.ToString()}:DATA"),
                    value: Encode(JObject.FromObject(new { transactions = new[] { btcTransaction.ToString() } }).ToString()),
                    version: BinaryData.Empty));
            }

            Mutation mutation = new Mutation(Encode(this.openChainUri.ToString()), records, BinaryData.Empty);

            byte[] serializedMutation = MessageSerializer.SerializeMutation(mutation);

            await SignAndSubmit(serializedMutation);
        }

        private async Task<byte[]> CreateTransaction(InboundTransaction transaction)
        {
            List<Record> records = new List<Record>();

            string issuanceAccount = $"/asset/{assetName}/{transaction.TransactionHash}/{transaction.OutputIndex}/";
            string asset = $"/asset/{assetName}/";
            string toAddress = $"/p2pkh/{transaction.Address}/";

            HttpClient client = new HttpClient();
            BinaryData issuanceKey = Encode($"{issuanceAccount}:ACC:{asset}");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={issuanceKey.ToString()}"));
            BinaryData issuanceVersion = BinaryData.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["version"]);

            if (issuanceVersion.Value.Count != 0)
                return null;

            BinaryData key = Encode($"{toAddress}:ACC:{asset}");
            getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={key.ToString()}"));
            JObject toAccount = JObject.Parse(await getValueResponse.Content.ReadAsStringAsync());
            BinaryData version = BinaryData.Parse((string)toAccount["version"]);
            long currentToBalance = BitConverter.ToInt64(BinaryData.Parse((string)toAccount["value"]).Value.Reverse().ToArray(), 0);

            records.Add(new Record(
                key: issuanceKey,
                value: new BinaryData(BitConverter.GetBytes(-transaction.Amount).Reverse().ToArray()),
                version: BinaryData.Empty));

            records.Add(new Record(
                key: key,
                value: new BinaryData(BitConverter.GetBytes(currentToBalance + transaction.Amount).Reverse().ToArray()),
                version: version));

            Mutation mutation = new Mutation(
                Encode(this.openChainUri.ToString()),
                records,
                BinaryData.Empty);

            return MessageSerializer.SerializeMutation(mutation);
        }

        public async Task<IList<OutboundTransaction>> GetUnprocessedTransactions()
        {
            string account = $"/asset/{assetName}/out/";
            string asset = $"/asset/{assetName}/";

            HttpClient client = new HttpClient();
            BinaryData key = Encode($"{account}:ACC:{asset}");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={key.ToString()}"));
            BinaryData currentVersion = BinaryData.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["version"]);

            TransactionCache cache = new TransactionCache(openChainUri, assetName);

            List<OutboundTransaction> result = new List<OutboundTransaction>();

            while (!currentVersion.Equals(BinaryData.Empty))
            {
                Mutation mutation = await cache.GetMutation(currentVersion);

                Record record = mutation.Records.FirstOrDefault(item => item.Key.Equals(key));
                long balance = BitConverter.ToInt64(record.Value.Value.Reverse().ToArray(), 0);

                long balanceChange;
                if (record.Version.Equals(BinaryData.Empty))
                {
                    balanceChange = balance;
                }
                else
                {
                    Mutation previousMutation = await cache.GetMutation(record.Version);
                    Record previousRecord = mutation.Records.FirstOrDefault(item => item.Key.Equals(key));
                    long previousBalance = BitConverter.ToInt64(previousRecord.Value.Value.Reverse().ToArray(), 0);

                    balanceChange = balance - previousBalance;
                }

                if (balanceChange < 0)
                    continue;

                BinaryData spendingKey = Encode($"/assets/{assetName}/processed/{currentVersion.ToString()}/:DATA");

                getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={spendingKey.ToString()}"));
                BinaryData processedValue = BinaryData.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["value"]);
                if (processedValue.Equals(BinaryData.Empty))
                {
                    string payingAddress = GetPayingAddress(mutation);
                    result.Add(new OutboundTransaction(payingAddress, null, balanceChange, currentVersion));
                }
                
                currentVersion = record.Version;
            }

            return result;
        }

        private string GetPayingAddress(Mutation outgoingTransaction)
        {
            string asset = $"/asset/{assetName}/";

            foreach (Record record in outgoingTransaction.Records)
            {
                string[] parts = Encoding.UTF8.GetString(record.Key.ToByteArray()).Split(':');
                if (parts.Length == 3 && parts[1] == "ACC" && parts[2] == asset)
                {
                    Match match = accountRegex.Match(parts[0]);
                    if (match.Success)
                    {
                        return match.Groups["address"].Value;
                    }
                }
            }

            return null;
        }

        private static BinaryData Encode(string data)
        {
            return new BinaryData(Encoding.UTF8.GetBytes(data));
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

            public Dictionary<BinaryData, Mutation> Transactions { get; } = new Dictionary<BinaryData, Mutation>();

            public async Task<Mutation> GetMutation(BinaryData hash)
            {
                Mutation mutation;
                if (!Transactions.TryGetValue(hash, out mutation))
                {
                    string account = $"/asset/{assetName}/out/";
                    string asset = $"/asset/{assetName}/";

                    HttpClient client = new HttpClient();
                    BinaryData key = new BinaryData(Encoding.UTF8.GetBytes($"{account}:ACC:{asset}"));

                    HttpResponseMessage getTransactionResponse = await client.GetAsync(new Uri(openChainUri, $"query/transaction?mutationHash={hash.ToString()}"));
                    JToken rawTransaction = JToken.Parse(await getTransactionResponse.Content.ReadAsStringAsync());

                    Transaction transaction = MessageSerializer.DeserializeTransaction(BinaryData.Parse((string)rawTransaction["raw"]));
                    mutation = MessageSerializer.DeserializeMutation(transaction.Mutation);

                    Transactions.Add(key, mutation);
                }

                return mutation;
            }
        }
    }
}
