using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Framework.Configuration;
using Microsoft.Framework.DependencyInjection;
using OpenChain.BitcoinGateway;

namespace OpenChainBitcoinGateway
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            ServiceCollection services = new ServiceCollection();
            ConfigurationBuilder builder = new ConfigurationBuilder();

            services.AddInstance<BitcoinClient>(new BitcoinClient(new Uri("https://api.coinprism.com/v1/"), null, null));

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Console.ReadLine();
        }
    }
}
