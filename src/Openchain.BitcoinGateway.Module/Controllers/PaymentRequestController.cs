using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using Microsoft.Extensions.Logging;

namespace Openchain.BitcoinGateway.Module.Controllers
{
    [Route("")]
    public class PaymentRequestController : Controller
    {
        private readonly ILogger logger;

        public PaymentRequestController(ILogger logger)
        {
            this.logger = logger;
        }
    }
}
