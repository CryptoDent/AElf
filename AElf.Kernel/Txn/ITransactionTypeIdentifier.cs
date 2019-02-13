namespace AElf.Kernel.Txn
{
    public interface ITransactionTypeIdentifier
    {
        bool IsSystemTransaction(Transaction transaction);
        bool CanBeBroadCast(Transaction transaction);
    }
}