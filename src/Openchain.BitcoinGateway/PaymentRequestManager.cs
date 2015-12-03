using System;
using System.Linq;
using System.Text;
using Bitcoin.PaymentProtocol;
using Google.Protobuf;
using NBitcoin;

namespace Openchain.BitcoinGateway
{
    public class PaymentRequestManager
    {
        private readonly ulong dustValue = 1500;
        private readonly bool isMainNet;
        private readonly BitcoinAddress destinationAddress;

        public PaymentRequestManager(bool isMainNet, string destinationAddress)
        {
            this.isMainNet = isMainNet;
            this.destinationAddress = BitcoinAddress.Create(destinationAddress);
        }

        public ByteString GetPaymentRequest(string finalAccount, ulong amount)
        {
            PaymentDetails paymentDetails = new PaymentDetails();
            paymentDetails.Network = isMainNet ? "main" : "test";
            paymentDetails.Time = GetTimestamp(DateTime.UtcNow);
            paymentDetails.Expires = GetTimestamp(DateTime.UtcNow.AddHours(1));
            paymentDetails.Memo = $"Funding Openchain account {finalAccount}";
            paymentDetails.PaymentUrl = "http://192.168.0.20:8080/payment";

            Output paymentOutput = new Output();
            paymentOutput.Amount = amount;
            paymentOutput.Script = Google.Protobuf.ByteString.CopyFrom(NBitcoin.Script.CreateFromDestinationAddress(destinationAddress).ToBytes());

            Output dataOutput = new Output();
            dataOutput.Amount = dustValue;
            dataOutput.Script = Google.Protobuf.ByteString.CopyFrom(
                new[] { (byte)OpcodeType.OP_RETURN }.Concat(Op.GetPushOp(Encoding.UTF8.GetBytes("OG" + finalAccount)).ToBytes()).ToArray());

            paymentDetails.Outputs.Add(paymentOutput);
            paymentDetails.Outputs.Add(dataOutput);

            PaymentRequest request = new PaymentRequest();
            request.SerializedPaymentDetails = paymentDetails.ToByteString();
            request.PkiType = "none";

            return new ByteString(request.ToByteArray());
        }

        private static ulong GetTimestamp(DateTime date)
        {
            return (ulong)(date - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
