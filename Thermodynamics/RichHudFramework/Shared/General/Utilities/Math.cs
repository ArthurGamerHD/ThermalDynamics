using System;
using VRageMath;

namespace RichHudFramework
{
	public static class MathUtils
    {
/// <summary>FloatToInt32Bits operation.</summary>
        public static uint FloatToInt32Bits(float value, bool invertSignBit = false)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            ulong sign = bits >> 63;
            ulong exp = (bits >> 52) & 0x7FFUL;
            ulong mant = bits & 0xFFFFFFFFFFFFFUL;
            int trueExp = (int)exp - 1023;
            
            int clampedTrueExp = Math.Max(-126, Math.Min(trueExp, 127));

            if (invertSignBit)
                sign = ~sign & 1ul;

            ulong singleExp = (ulong)(clampedTrueExp + 127) << 23;
            ulong singleMant = mant >> 29;
            ulong singleSign = sign << 31;
            uint singleBits = (uint)(singleSign | singleExp | singleMant);

            return singleBits;
        }

/// <summary>Int32ToFloat operation.</summary>
        public static float Int32ToFloat(uint bits, bool isSignInverted = false)
        {
            ulong sign = (ulong)(bits >> 31);
            ulong exp = (ulong)(bits >> 23) & 0xFFUL;
            ulong mant = (ulong)(bits & 0x7FFFFFU);

            if (isSignInverted)
                sign = ~sign & 1ul;

            ulong doubleSign = sign << 63;
            ulong doubleMant = mant << 29;
            uint originalExp = (uint)exp - 127;
            ulong doubleExp = (ulong)(originalExp + 1023) << 52;

            ulong doubleBits = doubleSign | doubleExp | doubleMant;
            double dValue = BitConverter.Int64BitsToDouble((long)doubleBits);

            return (float)dValue;
        }
    }
}
