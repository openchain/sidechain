using System.Collections.Generic;
using Microsoft.AspNet.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NBitcoin;

namespace Openchain.BitcoinGateway.Module.Controllers
{
    [Route("")]
    public class PaymentRequestController : Controller
    {
        private readonly ILogger logger;
        private readonly PegGateway gateway;
        private readonly PaymentRequestManager manager;

        public PaymentRequestController(ILogger logger, PegGateway gateway)
        {
            this.logger = logger;
            this.gateway = gateway;
            this.manager = new PaymentRequestManager(gateway.BitcoinClient.Network.Name == Network.Main.Name, gateway.BitcoinClient.ReceivingAddress);
        }

        [HttpGet("fund")]
        public ActionResult Fund(string address, ulong amount)
        {
            ByteString request = manager.GetPaymentRequest(address, amount);

            return this.File(request.ToByteArray(), "application/bitcoin-paymentrequest");
        }

        [HttpGet("")]
        public ActionResult Index()
        {
            return Content(
                "<html><body><a href='bitcoin:?r=http%3A%2F%2F192.168.0.20%3A8080%2Ffund%3Faddress%3D%2Fp2pkh%2FXat6UaXpQE9Dxv6rLtxY1peBkzC1SQDiEX%2F%26amount%3D150000'>Send</a></body></html>",
                MediaTypeHeaderValue.Parse("text/html"));
        }
    }
}
