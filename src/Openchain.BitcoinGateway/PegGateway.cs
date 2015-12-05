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

                await Task.Delay(TimeSpan.FromSeconds(5));
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

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
