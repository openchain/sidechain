using Microsoft.AspNet.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Openchain.BitcoinGateway.Module.Controllers
{
    [Route("")]
    public class PaymentRequestController : Controller
    {
        private readonly ILogger logger;
        private readonly PegGateway gateway;

        public PaymentRequestController(ILogger logger, PegGateway gateway)
        {
            this.logger = logger;
            this.gateway = gateway;
        }

        [HttpGet("fund")]
        public ActionResult Fund(string address, ulong amount)
        {
            PaymentRequestManager manager = new PaymentRequestManager(gateway.BitcoinClient.Network.Name == "Mainnet", gateway.BitcoinClient.ReceivingAddress);
            ByteString request = manager.GetPaymentRequest(address, amount);

            return this.File(request.ToByteArray(), "application/bitcoin-paymentrequest");
        }

        [HttpGet("")]
        public ActionResult Index()
        {
            return Content(
                "<html><body><a href='bitcoin:?r=http%3A%2F%2F192.168.0.20%3A8080%2Ffund%3Faddress%3D%2Fp2pkh%2F1AY9qB6RJ5FRk2cyPCAjE6zK5NXVjYMbro%2F%26amount%3D150000'>Send</a></body></html>",
                MediaTypeHeaderValue.Parse("text/html"));
        }
    }
}
