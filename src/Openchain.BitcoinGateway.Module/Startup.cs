using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Openchain.BitcoinGateway.Module
{
    public class Startup
    {
        private List<Task> workers = new List<Task>();

        public void ConfigureServices(IServiceCollection services)
        {
            // Setup logging
            services.BuildServiceProvider().GetService<ILoggerFactory>().AddConsole();
            services.AddTransient<ILogger>(serviceProvider => serviceProvider.GetService<ILoggerFactory>().CreateLogger("General"));

            // Setup configuration
            services.AddSingleton<IConfiguration>(_ =>
                new ConfigurationBuilder().SetBasePath(".").AddJsonFile("config.json").Build());

            // Setup ASP.NET MVC
            services
                .AddMvcCore()
                .AddViews()
                .AddJsonFormatters();

            // CORS Headers
            services.AddCors();
        }

        public void Configure(IApplicationBuilder app, ILogger logger, IConfiguration config)
        {
            app.UseCors(builder => builder.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

            app.UseIISPlatformHandler();

            // Add MVC to the request pipeline.
            app.UseMvc();

            this.workers.Add(RunGateway(logger, config));
        }

        private async Task RunGateway(ILogger logger, IConfiguration config)
        {
            Key gatewayKey = Key.Parse(config["gateway_key"], Network.TestNet);
            Key storageKey = Key.Parse(config["storage_key"], Network.TestNet);

            logger.LogInformation($"Initializing OC-Bitcoin-Gateway with address {gatewayKey.PubKey.GetAddress(Network.TestNet)}");

            BitcoinClient bitcoin = new BitcoinClient(new Uri(config["bitcoin_api_url"]), gatewayKey, storageKey, Network.TestNet);
            OpenchainClient openChain = new OpenchainClient(gatewayKey, "btc", new Uri(config["openchain_server"]));
            PegGateway gateway = new PegGateway(bitcoin, openChain, logger);

            logger.LogInformation("Starting gateway...");

            await gateway.OpenchainToBitcoin();
        }
    }
}
