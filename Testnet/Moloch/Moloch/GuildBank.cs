using System;
using System.Collections.Generic;
using System.Text;
using Stratis.SmartContracts;
using Stratis.SmartContracts.Standards;

namespace Moloch
{
    // todo: how does inheritance work for contracts? do i need this approved seperately as well?
    public class GuildBank : Ownable
    {   
        public Address ApprovedTokenAddress
        {
            get
            {
                return this.PersistentState.GetAddress("ApprovedTokenAddress");
            }
            private set
            {
                this.PersistentState.SetAddress("ApprovedTokenAddress", value);
            }
        }

        // todo: how to wrap calls to IStandardtoken in an easy way? Do I need to write a wrapper that uses .Call? I would like to have something like IContractReference<IStandardToken> that does all that for me but can it be done?
        public IStandardToken ApprovedStandardToken { get; set; }

        public GuildBank(ISmartContractState state, Address approvedTokenAddress, IStandardToken approvedStandardToken) : base(state)
        {
            ApprovedTokenAddress = approvedTokenAddress;
            ApprovedStandardToken = approvedStandardToken;
        }

        public bool Withdraw(Address receiver, ulong shares, ulong totalShares)
        {
            AssertOnlyOwner();

            // todo: correct equivalent of address(this)?  
            // todo: does it work like that in stratis with inheritance?
            ulong balance = ApprovedStandardToken.GetBalance(this.Address);
            ulong myShares = balance.Mul(shares);
            ulong amount = myShares.Div(totalShares);

            LogWithdrawal(receiver, amount);

            return ApprovedStandardToken.TransferTo(receiver, amount);
        }

        public Address GetAddress()
        {
            return this.Address;
        }

        private void LogWithdrawal(Address receiver, ulong amount)
        {
            Log(new WithdrawalLog { Receiver = receiver, Amount = amount });
        }

        public struct WithdrawalLog
        {
            [Index]
            public Address Receiver;

            public ulong Amount;
        }
    }
}