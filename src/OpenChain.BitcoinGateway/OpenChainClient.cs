using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
            await client.PostAsync(new Uri(openChainUri, "submit"), content);
        }

        private async Task<byte[]> CreateTransaction(InboundTransaction transaction)
        {
            List<Record> records = new List<Record>();
            
            string issuanceAccount = $"/asset/{assetName}/{transaction.TransactionHash}/{transaction.OutputIndex}";
            string asset = $"/asset/{assetName}";
            string toAddress = $"/p2pkh/{transaction.Address}";

            HttpClient client = new HttpClient();
            BinaryData issuanceKey = new BinaryData(Encoding.UTF8.GetBytes($"{issuanceAccount}:ACC:{asset}"));
            HttpResponseMessage getValueResponse = await client.GetAsync(new Uri(openChainUri, $"value?key={issuanceKey.ToString()}"));
            BinaryData issuanceVersion = BinaryData.Parse((string)JObject.Parse(await getValueResponse.Content.ReadAsStringAsync())["version"]);

            if (issuanceVersion.Value.Count != 0)
                return null;

            BinaryData key = new BinaryData(Encoding.UTF8.GetBytes($"{toAddress}:ACC:{asset}"));
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
                new BinaryData(Encoding.UTF8.GetBytes(this.openChainUri.ToString())),
                records,
                BinaryData.Empty);

            return MessageSerializer.SerializeMutation(mutation);
        }
    }
}
