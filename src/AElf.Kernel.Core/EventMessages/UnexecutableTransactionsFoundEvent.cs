using System.Collections.Generic;
using AElf.Common;

namespace AElf.Kernel.EventMessages
{
    public sealed class UnexecutableTransactionsFoundEvent
    {
        public UnexecutableTransactionsFoundEvent(BlockHeader header, List<Hash> transactions)
        {
            BlockHeader = header;
            Transactions = transactions;
        }

        public BlockHeader BlockHeader { get; }
        public List<Hash> Transactions { get; }
    }
}