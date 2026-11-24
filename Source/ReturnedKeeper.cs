using RimWorld;
using Verse;

namespace Cultist;

public sealed class ReturnedKeeperProperties : HediffCompProperties
{
    public ReturnedKeeperProperties() => compClass = typeof(ReturnedKeeperAge);
}

public sealed class ReturnedKeeperAge : HediffComp
{
    private long ageAtReturn = -1;
    private int nextCheck;

    public override void CompExposeData()
    {
        Scribe_Values.Look(ref ageAtReturn, "ageAtReturn", -1L);
        Scribe_Values.Look(ref nextCheck, "nextAgeCheck");
    }

    public override void CompPostPostAdd(DamageInfo? dinfo) => SettleAge();

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        int now = Find.TickManager.TicksGame;
        if (now < nextCheck)
            return;
        nextCheck = now + GenDate.TicksPerHour;
        SettleAge();
    }

    private void SettleAge()
    {
        Pawn_AgeTracker age = Pawn.ageTracker;
        if (age == null || age.AgeBiologicalTicks < age.AdultMinAgeTicks)
            return;

        long actual = age.AgeBiologicalTicks;
        if (ageAtReturn < 0 || ageAtReturn > actual)
            ageAtReturn = actual;
        else if (ageAtReturn < actual)
            age.AgeBiologicalTicks = ageAtReturn;
    }
}
