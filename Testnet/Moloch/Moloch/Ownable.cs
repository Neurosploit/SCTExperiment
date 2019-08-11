namespace Moloch
{
    using Stratis.SmartContracts;

    // todo: these are new smart contracts do they seperately need to be approved? How does that process work?
    // todo: what to do with internal keyword?
    public class Ownable : SmartContract
    {
        public Address Owner
        {
            get
            {
                return this.PersistentState.GetAddress("Owner");
            }
            private set
            {
                this.PersistentState.SetAddress("Owner", value);
            }
        }

        public Ownable(ISmartContractState state) : base(state)
        {
            Owner = state.Message.Sender;
                        
            LogOwnershipTransfer(Address.Zero, Owner);
        }

        private void LogOwnershipTransfer(Address previousOwner, Address newOwner)
        {
            Log(new OwnershipTransferLog { PreviousOwner = previousOwner, NewOwner = newOwner });
        }

        public void AssertOnlyOwner()
        {
            Assert(IsOwner());
        }

        public bool IsOwner()
        {
            return Message.Sender == Owner;
        }


        public void RenounceOwnership()
        {
            AssertOnlyOwner();

            LogOwnershipTransfer(Owner, Address.Zero);

            Owner = Address.Zero;
        }

        public void TransferOwnership(Address newOwner)
        {
            AssertOnlyOwner();

            TransferOwnershipInternal(newOwner);
        }

        // todo: what to do with internal keyword?
        internal void TransferOwnershipInternal(Address newOwner)
        {
            Assert(newOwner != Address.Zero);

            LogOwnershipTransfer(Owner, newOwner);
            Owner = newOwner;
        }

        public struct OwnershipTransferLog
        {
            [Index]
            public Address PreviousOwner;

            [Index]
            public Address NewOwner;
        }
    }
}
