using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Core;

public enum Suit
{
    Man,
    Pin,
    Sou,
    Wind,
    Dragon,
}

public enum WindTile
{
    East = 0,
    South = 1,
    West = 2,
    North = 3,
}

public enum DragonTile
{
    White = 0,
    Green = 1,
    Red = 2,
}

/// <summary>
/// One of the 34 distinct tile kinds, using the standard 0-33 numbering
/// (0-8 man, 9-17 pin, 18-26 sou, 27-30 winds, 31-33 dragons). This
/// numbering is common mahjong-notation convention, not specific to any
/// implementation.
/// </summary>
public readonly record struct Tile
{
    public const int KindCount = 34;

    public int Id { get; }

    /// <summary>True for a red-five akadora tile. Only meaningful when <see cref="Rank"/> is 5 in a suited tile.</summary>
    public bool IsRedFive { get; }

    public Tile(int id, bool isRedFive = false)
    {
        if (id is < 0 or >= KindCount)
            throw new ArgumentOutOfRangeException(nameof(id), id, $"Tile id must be 0-{KindCount - 1}.");
        if (isRedFive && !IsSuitedFive(id))
            throw new ArgumentException("IsRedFive only applies to a suited 5 (man/pin/sou).", nameof(isRedFive));

        Id = id;
        IsRedFive = isRedFive;
    }

    private static bool IsSuitedFive(int id) => id < 27 && id % 9 == 4;

    public Suit Suit => Id switch
    {
        < 9 => Suit.Man,
        < 18 => Suit.Pin,
        < 27 => Suit.Sou,
        < 31 => Suit.Wind,
        _ => Suit.Dragon,
    };

    /// <summary>1-9 for suited tiles, 1-4 for winds (East..North), 1-3 for dragons (White..Red).</summary>
    public int Rank => Suit switch
    {
        Suit.Man => Id + 1,
        Suit.Pin => Id - 9 + 1,
        Suit.Sou => Id - 18 + 1,
        Suit.Wind => Id - 27 + 1,
        Suit.Dragon => Id - 31 + 1,
        _ => throw new InvalidOperationException(),
    };

    public bool IsSuited => Suit is Suit.Man or Suit.Pin or Suit.Sou;
    public bool IsHonor => !IsSuited;
    public bool IsTerminal => IsSuited && (Rank == 1 || Rank == 9);
    public bool IsTerminalOrHonor => IsTerminal || IsHonor;

    public WindTile AsWind => Suit == Suit.Wind
        ? (WindTile)(Rank - 1)
        : throw new InvalidOperationException($"{this} is not a wind tile.");

    public DragonTile AsDragon => Suit == Suit.Dragon
        ? (DragonTile)(Rank - 1)
        : throw new InvalidOperationException($"{this} is not a dragon tile.");

    public static Tile FromSuitRank(Suit suit, int rank, bool isRedFive = false)
    {
        int baseId = suit switch
        {
            Suit.Man => 0,
            Suit.Pin => 9,
            Suit.Sou => 18,
            Suit.Wind => 27,
            Suit.Dragon => 31,
            _ => throw new ArgumentOutOfRangeException(nameof(suit)),
        };
        int maxRank = suit is Suit.Man or Suit.Pin or Suit.Sou ? 9 : suit == Suit.Wind ? 4 : 3;
        if (rank < 1 || rank > maxRank)
            throw new ArgumentOutOfRangeException(nameof(rank), rank, $"{suit} rank must be 1-{maxRank}.");
        return new Tile(baseId + rank - 1, isRedFive);
    }

    /// <summary>Next tile in the same suit, wrapping 9→1 (dora-indicator succession rule). Honor tiles wrap within their own wind/dragon cycle.</summary>
    public Tile NextForDora() => Suit switch
    {
        Suit.Man or Suit.Pin or Suit.Sou => FromSuitRank(Suit, Rank == 9 ? 1 : Rank + 1),
        Suit.Wind => FromSuitRank(Suit.Wind, Rank == 4 ? 1 : Rank + 1),
        Suit.Dragon => FromSuitRank(Suit.Dragon, Rank == 3 ? 1 : Rank + 1),
        _ => throw new InvalidOperationException(),
    };

    public override string ToString() => Suit switch
    {
        Suit.Man => $"{Rank}m{(IsRedFive ? "r" : "")}",
        Suit.Pin => $"{Rank}p{(IsRedFive ? "r" : "")}",
        Suit.Sou => $"{Rank}s{(IsRedFive ? "r" : "")}",
        Suit.Wind => AsWind.ToString(),
        Suit.Dragon => AsDragon.ToString(),
        _ => Id.ToString(),
    };
}
