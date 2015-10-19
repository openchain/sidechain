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

namespace Openchain.BitcoinGateway
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
                transaction = new ByteString(serializedMutation).ToString(),
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
                    key: Encode($"/asset/{assetName}/processed/{transaction.MutationHash.ToString()}:DATA"),
                    value: Encode(JObject.FromObject(new { transactions = new[] { btcTransaction.ToString() } }).ToString()),
                    version: ByteString.Empty));
            }

            Mutation mutation = new Mutation(Encode(this.openChainUri.ToString()), records, ByteString.Empty);

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
            ByteString issuanceKey = Encode($"{issuanceAccount}:ACC:{asset}");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={issuanceKey.ToString()}"));
            ByteString issuanceVersion = ByteString.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["version"]);

            if (issuanceVersion.Value.Count != 0)
                return null;

            ByteString key = Encode($"{toAddress}:ACC:{asset}");
            getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={key.ToString()}"));
            JObject toAccount = JObject.Parse(await getValueResponse.Content.ReadAsStringAsync());
            ByteString version = ByteString.Parse((string)toAccount["version"]);
            long currentToBalance = BitConverter.ToInt64(ByteString.Parse((string)toAccount["value"]).Value.Reverse().ToArray(), 0);

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
            ByteString key = Encode($"{account}:ACC:{asset}");
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={key.ToString()}"));
            ByteString currentVersion = ByteString.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["version"]);

            TransactionCache cache = new TransactionCache(openChainUri, assetName);

            List<OutboundTransaction> result = new List<OutboundTransaction>();

            while (!currentVersion.Equals(ByteString.Empty))
            {
                Mutation mutation = await cache.GetMutation(currentVersion);

                Record record = mutation.Records.FirstOrDefault(item => item.Key.Equals(key));
                long balance = BitConverter.ToInt64(record.Value.Value.Reverse().ToArray(), 0);

                long balanceChange;
                if (record.Version.Equals(ByteString.Empty))
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

                ByteString spendingKey = Encode($"/assets/{assetName}/processed/{currentVersion.ToString()}/:DATA");

                getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={spendingKey.ToString()}"));
                ByteString processedValue = ByteString.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["value"]);
                if (processedValue.Equals(ByteString.Empty))
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
                string[] parts = Encoding.UTF8.GetString(record.Key.ToByteArray(), 0, record.Key.Value.Count).Split(':');
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

        private static ByteString Encode(string data)
        {
            return new ByteString(Encoding.UTF8.GetBytes(data));
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
                    ByteString key = new ByteString(Encoding.UTF8.GetBytes($"{account}:ACC:{asset}"));

                    HttpResponseMessage getTransactionResponse = await client.GetAsync(new Uri(openChainUri, $"query/transaction?mutationHash={hash.ToString()}"));
                    JToken rawTransaction = JToken.Parse(await getTransactionResponse.Content.ReadAsStringAsync());

                    Transaction transaction = MessageSerializer.DeserializeTransaction(ByteString.Parse((string)rawTransaction["raw"]));
                    mutation = MessageSerializer.DeserializeMutation(transaction.Mutation);

                    Transactions.Add(key, mutation);
                }

                return mutation;
            }
        }
    }
}
