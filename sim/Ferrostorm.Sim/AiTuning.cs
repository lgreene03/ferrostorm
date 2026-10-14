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
///
/// P8-17 (ADR-072) appends the air answer's two numbers to the PERSONALITY
/// family, beside the wave size they are the same kind of number as: how large
/// a home guard a commander keeps is its shape, not its strength, and F3 asks
/// every rung from Normal up to answer air alike. <see cref="AntiAirCap"/> caps
/// the anti-air units the commander builds once it has seen enemy aircraft, and
/// <see cref="AntiAirGarrison"/> is how many of them it holds at home rather
/// than sending out as harvester escorts. A rung row leaves both at 0, which is
/// not a number anything reads, exactly as it leaves the wave size.
///
/// P8-22 (ADR-079) appends what the commander SPENDS, split between the two
/// families by DR-14's rule. How much production a commander runs and how
/// patiently it turns a bank into more of it is macro, so the RUNG owns
/// <see cref="ProducerCap"/> (the most barracks, and separately the most
/// factories, it holds at once; decision D7's caps), <see cref="SurplusReserve"/>
/// (the credits it keeps back from everything it buys out of surplus) and
/// <see cref="SurplusWindowTicks"/> (how long its treasury must have stood at
/// or above that reserve, unbroken, before the surplus counts as sustained).
/// How many faction defences guard a base is a commander's taste, beside the
/// home guard the wave size already sets, so the PERSONALITY owns
/// <see cref="FactionDefenceCap"/>. Each family leaves the other's at 0.
/// </summary>
public readonly record struct AiTuningDef(
    AiTuningKind Kind,
    int ActEvery,
    int WaveSize,
    int BeatNumerator,
    int BeatDenominator,
    int HarvestersPerRefinery,
    int StartingCreditHandicap,
    int AntiAirCap = 0,
    int AntiAirGarrison = 0,
    int ProducerCap = 0,
    int SurplusReserve = 0,
    int SurplusWindowTicks = 0,
    int FactionDefenceCap = 0);

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
    // P8-17 (ADR-072): the last two numbers of a personality are its anti-air
    // cap and its anti-air garrison. The cap is the personality's own wave size,
    // so no commander ever holds more anti-air than one wave's worth of army;
    // the garrison is 2, the floor the ground garrison already keeps.
    // P8-22 (ADR-079): a personality's faction defence cap is its own ground
    // garrison, half its wave with a floor of 2 (3, 2 and 5), so a base keeps
    // as many signature guns as it keeps fighters at home. A rung's producer
    // cap is decision D7's (Easy 1, Normal 2, Hard 3, Brutal 3); its reserve is
    // 4500, the superweapon rung's own credit test and the dearest in the
    // ladder (no surplus purchase is ordered on credits the reserve keeps, but
    // a beat can still end below it, ADR-079 section 2); and its window is 4500
    // ticks, the far edge of the measured gap between a passing bank and a
    // sustained one (spendprobe at the base: of 423 stretches at or above 4500
    // that ended before their match, one lasted between two and five minutes;
    // the near edge, 1800, failed F8's launch half in pillargate).
    public static readonly AiTuningDef Standard = new(AiTuningKind.Personality, 15, 6, 1, 1, 1, 0, 6, 2, FactionDefenceCap: 3);   // ai_standard
    public static readonly AiTuningDef Rusher = new(AiTuningKind.Personality, 15, 4, 1, 1, 1, 0, 4, 2, FactionDefenceCap: 2);     // ai_rusher
    public static readonly AiTuningDef Turtle = new(AiTuningKind.Personality, 15, 10, 1, 1, 1, 0, 10, 2, FactionDefenceCap: 5);   // ai_turtle
    public static readonly AiTuningDef Easy = new(AiTuningKind.Rung, 0, 0, 2, 1, 1, 0, ProducerCap: 1, SurplusReserve: 4500, SurplusWindowTicks: 4500);       // ai_easy
    public static readonly AiTuningDef Normal = new(AiTuningKind.Rung, 0, 0, 1, 1, 1, 0, ProducerCap: 2, SurplusReserve: 4500, SurplusWindowTicks: 4500);     // ai_normal
    public static readonly AiTuningDef Hard = new(AiTuningKind.Rung, 0, 0, 1, 1, 2, 0, ProducerCap: 3, SurplusReserve: 4500, SurplusWindowTicks: 4500);       // ai_hard
    public static readonly AiTuningDef Brutal = new(AiTuningKind.Rung, 0, 0, 2, 3, 2, 5000, ProducerCap: 3, SurplusReserve: 4500, SurplusWindowTicks: 4500); // ai_brutal

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
