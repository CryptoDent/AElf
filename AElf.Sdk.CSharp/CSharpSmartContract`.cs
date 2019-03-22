using System;
using AElf.Common;
using AElf.Kernel;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Sdk;
using AElf.Sdk.CSharp.State;
using Google.Protobuf;

namespace AElf.Sdk.CSharp
{
    public partial class CSharpSmartContract<TContractState> where TContractState : ContractState, new()
    {
        private ISmartContractBridgeContext _context;

        public ISmartContractBridgeContext Context
        {
            get => _context;
            private set
            {
                _context = value;
                SetContractAddress(_context.Self);
            }
        }

        public TContractState State { get; internal set; }

        public CSharpSmartContract()
        {
            State = new TContractState();
        }
    }
}