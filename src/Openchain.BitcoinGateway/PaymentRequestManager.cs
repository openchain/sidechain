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
using System.IO;
using System.Linq;
using System.Text;
using Bitcoin.PaymentProtocol;
using Google.Protobuf;
using NBitcoin;

namespace Openchain.BitcoinGateway
{
    public class PaymentRequestManager
    {
        private readonly ulong dustValue = 1000;
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
