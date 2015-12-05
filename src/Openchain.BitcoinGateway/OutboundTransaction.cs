namespace Openchain.BitcoinGateway
{
    public class OutboundTransaction
    {
        public OutboundTransaction(ByteString recordKey, long amount, ByteString version, string target)
        {
            this.RecordKey = recordKey;
            this.Amount = amount;
            this.Version = version;
            this.Target = target;
        }

        public ByteString RecordKey { get; }

        public ByteString Version { get; }

        public long Amount { get; }

        public string Target { get; }
    }
}
