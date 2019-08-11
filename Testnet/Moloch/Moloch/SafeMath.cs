namespace Moloch
{    
    public static class SafeMath
    {        
        // todo: is ulong the stratis equivalent of uint256 or does that need to get ported as well?
        public static ulong Mul(this ulong a, ulong b)
        {
            // Gas optimization: this is cheaper than requiring 'a' not being zero, but the
            // benefit is lost if 'b' is also tested.
            // See: https://github.com/OpenZeppelin/openzeppelin-solidity/pull/522
            if (a == 0)
            {
                return 0;
            }

            ulong c = a * b;
            ContractHelper.Assert(c / a == b);

            return c;
        }

        public static ulong Div(this ulong a, ulong b)
        {
            ContractHelper.Assert(b > 0);

            ulong c = a / b;
            return c;
        }

        public static ulong Sub(this ulong a, ulong b)
        {
            ContractHelper.Assert(b <= a);

            ulong c = a - b;
            return c;
        }

        public static ulong Add(this ulong a, ulong b)
        {
            ulong c = a + b;

            ContractHelper.Assert(c >= a);

            return c;
        }

        public static ulong Mod(this ulong a, ulong b)
        {
            ContractHelper.Assert(b != 0);

            return a % b;
        }
    }
}
