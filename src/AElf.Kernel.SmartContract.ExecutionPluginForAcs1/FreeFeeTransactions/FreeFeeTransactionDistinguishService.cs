using System.Collections.Generic;
using System.Linq;
using AElf.Types;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.ExecutionPluginForAcs1.FreeFeeTransactions
{
    public class FreeFeeTransactionDistinguishService : IFreeFeeTransactionDistinguishService, ISingletonDependency
    {
        private readonly IEnumerable<IChargeFeeStrategy> _chargeFeeStrategies;

        public FreeFeeTransactionDistinguishService(IEnumerable<IChargeFeeStrategy> chargeFeeStrategies)
        {
            _chargeFeeStrategies = chargeFeeStrategies;
        }

        public bool IsFree(Transaction transaction)
        {
            return _chargeFeeStrategies
                .Where(chargeFeeStrategy => transaction.To == chargeFeeStrategy.ContractAddress &&
                                            (transaction.MethodName == chargeFeeStrategy.MethodName ||
                                             chargeFeeStrategy.MethodName == string.Empty))
                .All(chargeFeeStrategy => chargeFeeStrategy.IsFree(transaction));
        }
    }
}