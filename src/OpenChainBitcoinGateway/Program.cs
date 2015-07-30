using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Framework.Configuration;
using Microsoft.Framework.DependencyInjection;
using NBitcoin;
using OpenChain.BitcoinGateway;

namespace OpenChainBitcoinGateway
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            ServiceCollection services = new ServiceCollection();
            IConfigurationBuilder builder = new ConfigurationBuilder(".").AddJsonFile("config.json");
            IConfiguration config = builder.Build();
            
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Key key = Key.Parse(config["gateway_key"], Network.TestNet);

            Console.WriteLine($"Initializing OC-Bitcoin-Gateway with address {key.PubKey.GetAddress(Network.TestNet)}");

            BitcoinClient bitcoin = new BitcoinClient(new Uri(config["bitcoin_api_url"]), key, key, Network.TestNet);
            OpenChainClient openChain = new OpenChainClient(key, "btc", new Uri(config["openchain_server"]));
            PegGateway gateway = new PegGateway(bitcoin, openChain);

            gateway.GetBitcoinTransactions().Wait();

            Console.ReadLine();
        }
    }
}
