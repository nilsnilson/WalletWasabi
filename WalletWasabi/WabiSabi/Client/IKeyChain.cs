using NBitcoin;
using WalletWasabi.Crypto;

namespace WalletWasabi.WabiSabi.Client
{
	public interface IKeyChain
	{
		OwnershipProof GetOwnershipProof(IDestination destination, CoinJoinInputCommitmentData commitedData);
		Transaction Sign(Transaction transaction, Coin coin, OwnershipProof ownershipProof);
	}
}
