using System;
using AElf.Common;
using AElf.Contracts.MultiToken.Messages;
using AElf.Sdk.CSharp.State;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Resource
{
    public class TokenContractReferenceState : ContractReferenceState
    {
        internal MethodReference<TransferInput, Empty> Transfer { get; set; }
        internal MethodReference<TransferFromInput, Empty> TransferFrom { get; set; }
    }
    
    public class ResourceContractState : ContractState
    {
        public BoolState Initialized { get; set; }
        public MappedState<StringValue, Converter> Converters { get; set; }
        public MappedState<UserResourceKey, long> UserBalances { get; set; }
        public MappedState<UserResourceKey, long> LockedUserResources { get; set; }
        public TokenContractReferenceState TokenContract { get; set; }
        public ProtobufState<Address> FeeAddress { get; set; }
        public ProtobufState<Address> ResourceControllerAddress { get; set; }
    }
}