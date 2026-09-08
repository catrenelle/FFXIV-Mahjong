using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Rules.YakuRules;

namespace FFXIVMahjong.Rules;

/// <summary>The full yaku list from the official Doman Mahjong guide — see docs/ruleset.md.</summary>
public sealed class DomanRuleSet : IRuleSet
{
    public IReadOnlyList<IYakuRule> YakuRules { get; } =
    [
        new RiichiRule(), new DoubleRiichiRule(), new MenzenTsumoRule(), new IppatsuRule(),
        new HaiteiRule(), new HouteiRule(), new RinshanRule(), new ChankanRule(),

        new TanyaoRule(), new HonitsuRule(), new ChinitsuRule(), new HonroutouRule(),

        new YakuhaiRule(), new PinfuRule(), new IipeikoRule(), new RyanpeikouRule(),
        new SanshokuDoujunRule(), new IttsuRule(), new ChantaRule(), new JunchanRule(),
        new ToitoiRule(), new SanankouRule(), new SanshokuDoukouRule(), new SankantsuRule(),

        new ChiitoitsuRule(),

        new KokushiRule(), new SuuankouRule(), new DaisangenRule(), new ShousuushiiRule(),
        new DaisuushiiRule(), new TsuuiisouRule(), new ChinroutouRule(), new RyuuiisouRule(),
        new ChuurenpoutouRule(), new SuukantsuRule(), new TenhouRule(), new ChiihouRule(),
    ];
}
