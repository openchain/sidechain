using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Openchain.BitcoinGateway
{
    public class PegGateway
    {
        private readonly ILogger logger;

        public PegGateway(BitcoinClient bitcoinClient, OpenchainClient openChainClient, ILogger logger)
        {
            this.BitcoinClient = bitcoinClient;
            this.OpenchainClient = openChainClient;
            this.logger = logger;
        }

        public BitcoinClient BitcoinClient { get; }

        public OpenchainClient OpenchainClient { get; }

        public async Task BitcoinToOpenChain()
        {
            while (true)
            {
                try
                {
                    IList<InboundTransaction> transactions = await this.BitcoinClient.GetUnspentOutputs();

                    foreach (InboundTransaction transaction in transactions)
                    {
                        try
                        {
                            await OpenchainClient.AddAsset(transaction);
                            await BitcoinClient.MoveToStorage(transaction);
                        }
                        catch (Exception exception)
                        {
                            this.logger.LogError($"An exception occurred: {exception.ToString()}");
                        }
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
                    IList<OutboundTransaction> transactions = await this.OpenchainClient.GetUnprocessedTransactions();

                    if (transactions.Count > 0)
                    {
                        ByteString withdrawalTransaction = await this.BitcoinClient.IssueWithdrawal(transactions);

                        await this.OpenchainClient.MoveToRedemption(transactions, withdrawalTransaction);
                        await this.BitcoinClient.SubmitTransaction(withdrawalTransaction);
                    }
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
