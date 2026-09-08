using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Rules;

public interface IRuleSet
{
    IReadOnlyList<IYakuRule> YakuRules { get; }
}
