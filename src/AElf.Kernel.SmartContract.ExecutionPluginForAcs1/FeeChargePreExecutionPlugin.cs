using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.SmartContract.ExecutionPluginForAcs1.FreeFeeTransactions;
using AElf.Kernel.SmartContract.Sdk;
using AElf.Kernel.Token;
using AElf.Types;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.ExecutionPluginForAcs1
{
    public class FeeChargePreExecutionPlugin : IPreExecutionPlugin, ISingletonDependency
    {
        private readonly IHostSmartContractBridgeContextService _contextService;
        private readonly IPrimaryTokenSymbolProvider _primaryTokenSymbolProvider;
        private readonly ITransactionSizeFeeUnitPriceProvider _transactionSizeFeeUnitPriceProvider;
        private readonly IFreeFeeTransactionDistinguishService _freeFeeTransactionDistinguishService;

        public ILogger<FeeChargePreExecutionPlugin> Logger { get; set; }

        public FeeChargePreExecutionPlugin(IHostSmartContractBridgeContextService contextService,
            IPrimaryTokenSymbolProvider primaryTokenSymbolProvider,
            ITransactionSizeFeeUnitPriceProvider transactionSizeFeeUnitPriceProvider,
            IFreeFeeTransactionDistinguishService freeFeeTransactionDistinguishService)
        {
            _contextService = contextService;
            _primaryTokenSymbolProvider = primaryTokenSymbolProvider;
            _transactionSizeFeeUnitPriceProvider = transactionSizeFeeUnitPriceProvider;
            _freeFeeTransactionDistinguishService = freeFeeTransactionDistinguishService;

            Logger = NullLogger<FeeChargePreExecutionPlugin>.Instance;
        }

        private static bool IsAcs1(IReadOnlyList<ServiceDescriptor> descriptors)
        {
            return descriptors.Any(service => service.File.GetIndentity() == "acs1");
        }

        public async Task<IEnumerable<Transaction>> GetPreTransactionsAsync(
            IReadOnlyList<ServiceDescriptor> descriptors, ITransactionContext transactionContext)
        {
            try
            {
                var context = _contextService.Create();

                if (_freeFeeTransactionDistinguishService.IsFree(transactionContext.Transaction))
                {
                    return new List<Transaction>();
                }

                context.TransactionContext = transactionContext;
                var tokenContractAddress = context.GetContractAddressByName(TokenSmartContractAddressNameProvider.Name);

                if (context.CurrentHeight < Constants.GenesisBlockHeight + 1 || tokenContractAddress == null)
                {
                    return new List<Transaction>();
                }

                if (!IsAcs1(descriptors) && transactionContext.Transaction.To != tokenContractAddress)
                {
                    return new List<Transaction>();
                }

                var tokenStub = new TokenContractContainer.TokenContractStub
                {
                    __factory = new TransactionGeneratingOnlyMethodStubFactory
                    {
                        Sender = transactionContext.Transaction.From,
                        ContractAddress = tokenContractAddress
                    }
                };
                if (transactionContext.Transaction.To == tokenContractAddress &&
                    transactionContext.Transaction.MethodName == nameof(tokenStub.ChargeTransactionFees))
                {
                    // Skip ChargeTransactionFees itself 
                    return new List<Transaction>();
                }

                var unitPrice = await _transactionSizeFeeUnitPriceProvider.GetUnitPriceAsync(new ChainContext
                {
                    BlockHash = transactionContext.PreviousBlockHash,
                    BlockHeight = transactionContext.BlockHeight - 1
                });
                var chargeFeeTransaction = (await tokenStub.ChargeTransactionFees.SendAsync(
                    new ChargeTransactionFeesInput
                    {
                        MethodName = transactionContext.Transaction.MethodName,
                        ContractAddress = transactionContext.Transaction.To,
                        TransactionSizeFee = unitPrice * transactionContext.Transaction.Size(),
                        PrimaryTokenSymbol = await _primaryTokenSymbolProvider.GetPrimaryTokenSymbol()
                    })).Transaction;
                return new List<Transaction>
                {
                    chargeFeeTransaction
                };
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to generate ChargeTransactionFees tx.", e);
                throw;
            }
        }
    }
}