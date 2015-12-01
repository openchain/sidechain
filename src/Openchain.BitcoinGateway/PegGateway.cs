using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Openchain.BitcoinGateway
{
    public class PegGateway
    {
        private readonly BitcoinClient bitcoinClient;
        private readonly OpenchainClient openChainClient;
        private readonly ILogger logger;

        public PegGateway(BitcoinClient bitcoinClient, OpenchainClient openChainClient, ILogger logger)
        {
            this.bitcoinClient = bitcoinClient;
            this.openChainClient = openChainClient;
            this.logger = logger;
        }

        public async Task BitcoinToOpenChain()
        {
            while (true)
            {
                try
                {
                    IList<InboundTransaction> transactions = await this.bitcoinClient.GetUnspentOutputs();

                    foreach (InboundTransaction transaction in transactions)
                    {
                        await openChainClient.AddAsset(transaction);
                        await bitcoinClient.MoveToStorage(transaction);
                    }
                }
                catch (Exception exception)
                {
                    this.logger.LogError($"An exception occurred: {exception.ToString()}");
                }

                await Task.Delay(TimeSpan.FromMinutes(0.25));
            }
        }

        public async Task OpenchainToBitcoin()
        {
            while (true)
            {
                try
                {
                    IList<OutboundTransaction> transactions = await this.openChainClient.GetUnprocessedTransactions();
                    ByteString withdrawalTransaction = await this.bitcoinClient.IssueWithdrawal(transactions);
                    await this.openChainClient.MoveToRedemption(transactions, withdrawalTransaction);
                    await this.bitcoinClient.SubmitTransaction(withdrawalTransaction);
                }
                catch (Exception exception)
                {
                    this.logger.LogError($"An exception occurred: {exception.ToString()}");
                }

                await Task.Delay(TimeSpan.FromMinutes(0.25));
            }
        }
    }
}
