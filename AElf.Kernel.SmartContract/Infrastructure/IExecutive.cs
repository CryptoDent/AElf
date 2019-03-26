﻿using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Common;
using AElf.Kernel.SmartContract.Sdk;

namespace AElf.Kernel.SmartContract.Infrastructure
{
    public interface IExecutive
    {
        IExecutive SetMaxCallDepth(int maxCallDepth);
 
        IExecutive SetHostSmartContractBridgeContext(IHostSmartContractBridgeContext smartContractBridgeContext);
        IExecutive SetTransactionContext(ITransactionContext transactionContext);
        IExecutive SetStateProviderFactory(IStateProviderFactory stateProviderFactory);
        void SetDataCache(IStateCache cache); //temporary solution to let data provider access actor's state cache
        Task Apply();
        string GetJsonStringOfParameters(string methodName, byte[] paramsBytes);
        byte[] GetFileDescriptorSet();

    }
}
