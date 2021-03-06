using System;
using System.Threading.Tasks;
using AElf.Common;
using AElf.Types.CSharp;
using Google.Protobuf;

namespace AElf.Kernel.SmartContract.ExecutionPluginForAcs1
{
    public class TransactionGeneratingOnlyMethodStubFactory : IMethodStubFactory
    {
        public Address Sender { get; set; }
        public Address ContractAddress { get; set; }

        public IMethodStub<TInput, TOutput> Create<TInput, TOutput>(Method<TInput, TOutput> method)
            where TInput : IMessage<TInput>, new() where TOutput : IMessage<TOutput>, new()
        {
            async Task<IExecutionResult<TOutput>> SendAsync(TInput input)
            {
                return new ExecutionResult<TOutput>()
                {
                    Transaction = new Transaction()
                    {
                        From = Sender,
                        To = ContractAddress,
                        MethodName = method.Name,
                        Params = ByteString.CopyFrom(method.RequestMarshaller.Serializer(input))
                    }
                };
            }

            async Task<TOutput> CallAsync(TInput input)
            {
                throw new NotSupportedException();
            }

            return new MethodStub<TInput, TOutput>(method, SendAsync, CallAsync);
        }
    }
}