using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Cultist;

public sealed class CultistNamedFollowerDef : Def
{
    public string sourceId;
    public Aspect affinity = Aspect.Lantern;
    public Gender fixedGender = Gender.None;
    public HediffDef appearanceHediff;
    [MustTranslate] public string believerLabel;
    [MustTranslate] public string discipleLabel;
    [MustTranslate] public string exaltedLabel;
    public string iconPath;
    public bool isHunterConvert;

    public static IEnumerable<CultistNamedFollowerDef> ForAspect(Aspect aspect) =>
        DefDatabase<CultistNamedFollowerDef>.AllDefsListForReading.Where(def => def.affinity == aspect);
}
