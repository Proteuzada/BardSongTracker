using System;
using System.Globalization;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Decodes one effect pair from an ACT network ability line.
    /// The amount is not the literal number written in the log. The layout is the one
    /// documented for the ACT network log: the high 16 bits are the amount, unless the
    /// 0x4000 mask is set, in which case the amount is the 24-bit value formed by bytes
    /// D, A, B of the word ABCD. A 0x0100 mask is a hit that dealt no damage.
    /// </summary>
    public static class DamageCodec
    {
        public static long Amount(uint raw)
        {
            if ((raw & 0x0100) != 0)
                return 0;

            if ((raw & 0x4000) != 0)
            {
                var a = (raw >> 24) & 0xFF;
                var b = (raw >> 16) & 0xFF;
                var d = raw & 0xFF;
                return (long)((d << 16) | (a << 8) | b);
            }

            return raw >> 16;
        }

        public static bool TryHex(string text, out uint value)
        {
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Damage types on the low byte of the flags word. Heals, misses, and status
        /// applications are not damage.
        /// </summary>
        public static bool IsDamage(uint flags)
        {
            var kind = flags & 0xFF;
            return kind is 0x03 or 0x05 or 0x06;
        }
    }
}
