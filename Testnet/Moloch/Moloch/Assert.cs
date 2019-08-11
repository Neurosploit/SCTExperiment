namespace Moloch
{
    using Stratis.SmartContracts;

    public static class ContractHelper
    {
        // todo: would be nice to assert anywhere in the code, not just SmartContract implementers
        //note: taken from https://github.com/stratisproject/Stratis.SmartContracts/blob/f0497da72dd72c27d409da0f58732ea90b4dab76/Stratis.SmartContracts/SmartContract.cs#L101
        public static void Assert(bool condition, string message = "Assert failed.")
        {
            if (!condition)
            {
                throw new SmartContractAssertException(message);
            }
        }
    }
}
