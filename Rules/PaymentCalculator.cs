using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Rules;

/// <summary>
/// Splits a scored hand's points into actual table payments. Inferred from the Lodestone
/// guide's prose ("points divided equally among three opponents," "dealer receives 50%
/// bonus," "dealer pays double when losing") rather than an exact worked example — treat
/// as a hypothesis to confirm against real point deltas once we're watching a live game.
/// </summary>
public static class PaymentCalculator
{
    public readonly record struct TsumoPayment(int FromDealer, int FromEachNonDealer);

    public static TsumoPayment ForTsumo(int points, bool winnerIsDealer)
    {
        if (winnerIsDealer)
        {
            int total = points * 3 / 2;
            int each = total / 3;
            return new TsumoPayment(FromDealer: 0, FromEachNonDealer: each);
        }

        int share = points / 4;
        return new TsumoPayment(FromDealer: share * 2, FromEachNonDealer: share);
    }

    public static int ForRon(int points, bool winnerIsDealer) => winnerIsDealer ? points * 3 / 2 : points;
}
