using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Openchain.BitcoinGateway
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            ILogger logger = new LoggerFactory().AddConsole().CreateLogger("General");

            ServiceCollection services = new ServiceCollection();
            IConfigurationBuilder builder = new ConfigurationBuilder().SetBasePath(".").AddJsonFile("config.json");
            IConfiguration config = builder.Build();
            
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Key gatewayKey = Key.Parse(config["gateway_key"], Network.TestNet);
            Key storageKey = Key.Parse(config["storage_key"], Network.TestNet);

            logger.LogInformation($"Initializing OC-Bitcoin-Gateway with address {gatewayKey.PubKey.GetAddress(Network.TestNet)}");

            BitcoinClient bitcoin = new BitcoinClient(new Uri(config["bitcoin_api_url"]), gatewayKey, storageKey, Network.TestNet);
            OpenchainClient openChain = new OpenchainClient(gatewayKey, "btc", new Uri(config["openchain_server"]));
            PegGateway gateway = new PegGateway(bitcoin, openChain, logger);

            logger.LogInformation("Starting gateway...");

            gateway.OpenchainToBitcoin().Wait();

            Console.ReadLine();
        }
    }
}
