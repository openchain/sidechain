using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Framework.Configuration;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Logging;
using NBitcoin;
using OpenChain.BitcoinGateway;

namespace OpenChainBitcoinGateway
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            ILogger logger = new LoggerFactory().AddConsole().CreateLogger("General");

            ServiceCollection services = new ServiceCollection();
            IConfigurationBuilder builder = new ConfigurationBuilder(".").AddJsonFile("config.json");
            IConfiguration config = builder.Build();
            
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Key gatewayKey = Key.Parse(config["gateway_key"], Network.TestNet);
            Key storageKey = Key.Parse(config["storage_key"], Network.TestNet);

            logger.LogInformation($"Initializing OC-Bitcoin-Gateway with address {gatewayKey.PubKey.GetAddress(Network.TestNet)}");

            BitcoinClient bitcoin = new BitcoinClient(new Uri(config["bitcoin_api_url"]), gatewayKey, storageKey, Network.TestNet);
            OpenChainClient openChain = new OpenChainClient(gatewayKey, "btc", new Uri(config["openchain_server"]));
            PegGateway gateway = new PegGateway(bitcoin, openChain, logger);

            logger.LogInformation("Starting gateway...");

            gateway.OpenChainToBitcoin().Wait();

            Console.ReadLine();
        }
    }
}
