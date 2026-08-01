namespace Ferrostorm.Sim;

/// <summary>
/// Which family an AI tuning row belongs to. DR-14 keeps personality and
/// difficulty ORTHOGONAL - personality is a commander's taste in wave size,
/// difficulty is how good it is - and the two families therefore author
/// different keys. The discriminator is carried on the def rather than inferred
/// from the id so that a file landing in the wrong slot is refused at
/// registration instead of dividing by a beat denominator nobody authored.
/// </summary>
public enum AiTuningKind
{
    Personality = 0,
    Rung = 1,
}

/// <summary>
/// One row of the skirmish commander's tuning, as authored in /data/ai. One
/// shape serves both families and only the EFFECT differs, which is why there
/// is a single table, a single schema and a single registration step rather
/// than a parallel system per family.
///
/// A personality row owns <see cref="ActEvery"/> and <see cref="WaveSize"/> and
/// leaves the rung fields at their identity (a beat ratio of 1/1, one harvester
/// per refinery, no handicap). A rung row owns the other four and leaves the
/// personality fields at 0, which is not a number anything reads: composition
/// takes the beat and the economy from the rung and the shape from the
/// personality, so a rung's ActEvery is never consulted. The loader refuses a
/// file that authors the other family's keys, so the unowned fields can never
/// carry an authored value that is then silently dropped.
///
/// The beat is a RATIO rather than a multiplier because the ladder needs 2/3
/// exactly and no floating-point type may appear anywhere in /sim (CLAUDE.md's
/// determinism rule). The composed beat is `actEvery * numerator / denominator`
/// in integer arithmetic, truncating exactly as the compiled expression it
/// replaces did: 15 * 2 / 3 is 10, not 10.0.
/// </summary>
public readonly record struct AiTuningDef(
    AiTuningKind Kind,
    int ActEvery,
    int WaveSize,
    int BeatNumerator,
    int BeatDenominator,
    int HarvestersPerRefinery,
    int StartingCreditHandicap);

/// <summary>
/// The compiled reference table for the skirmish commander's tuning: the values
/// a /data/ai file must reproduce exactly, in the shape <see cref="Weapons"/> is
/// for guns. Static so that a caller with no World (the balance tool, the
/// client's own harnesses, the ~138 runner scenarios that build a bare World)
/// still gets today's commander; a live match must read World.GetAiTuning
/// instead, which honours RegisterAiTuning and is therefore what /data actually
/// drives.
///
/// The design reasoning that used to sit beside these numbers in SkirmishAI.cs
/// now lives in the files, beside the numbers a designer can actually edit.
/// </summary>
public static class AiTuning
{
    /// <summary>Tuning ids are the sim's wire identity for a row, exactly as a
    /// weapon id is: the file names the thing and this names the number. They
    /// are dense from 1 so the /data completeness check can walk them, and the
    /// three personalities come before the four rungs so the table reads in the
    /// order a player picks - taste, then strength.</summary>
    public const int StandardId = 1;
    public const int RusherId = 2;
    public const int TurtleId = 3;
    public const int EasyId = 4;
    public const int NormalId = 5;
    public const int HardId = 6;
    public const int BrutalId = 7;

    /// <summary>The highest compiled tuning id, and therefore the bound for
    /// every loop that enumerates the table (World's seed, the /data loader's
    /// completeness check).</summary>
    public const int MaxTuningId = BrutalId;

    // Each def below is the reference copy of one file in data/ai, named in the
    // trailing comment.
    public static readonly AiTuningDef Standard = new(AiTuningKind.Personality, 15, 6, 1, 1, 1, 0);   // ai_standard
    public static readonly AiTuningDef Rusher = new(AiTuningKind.Personality, 15, 4, 1, 1, 1, 0);     // ai_rusher
    public static readonly AiTuningDef Turtle = new(AiTuningKind.Personality, 15, 10, 1, 1, 1, 0);    // ai_turtle
    public static readonly AiTuningDef Easy = new(AiTuningKind.Rung, 0, 0, 2, 1, 1, 0);               // ai_easy
    public static readonly AiTuningDef Normal = new(AiTuningKind.Rung, 0, 0, 1, 1, 1, 0);             // ai_normal
    public static readonly AiTuningDef Hard = new(AiTuningKind.Rung, 0, 0, 1, 1, 2, 0);               // ai_hard
    public static readonly AiTuningDef Brutal = new(AiTuningKind.Rung, 0, 0, 2, 3, 2, 5000);          // ai_brutal

    /// <summary>The compiled reference row for an id. Throws rather than
    /// defaulting, and that is deliberate: a weapon id can be 0 for "unarmed",
    /// but there is no such thing as an unspecified commander, and a quiet
    /// default here would be a beat of zero the sim divides by.</summary>
    public static AiTuningDef Get(int tuningId) => tuningId switch
    {
        StandardId => Standard,
        RusherId => Rusher,
        TurtleId => Turtle,
        EasyId => Easy,
        NormalId => Normal,
        HardId => Hard,
        BrutalId => Brutal,
        _ => throw new FormatException($"unknown AI tuning id {tuningId}"),
    };

    /// <summary>The rung row a difficulty selects. The enum is the player-facing
    /// pick and this is its tuning id, kept here beside the table rather than in
    /// SkirmishAI so the loader, the world and the commander all agree on one
    /// mapping.</summary>
    public static int RungIdOf(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => EasyId,
        AiDifficulty.Hard => HardId,
        AiDifficulty.Brutal => BrutalId,
        _ => NormalId,
    };
}
