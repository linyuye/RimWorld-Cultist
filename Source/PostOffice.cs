using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private int nextPostOfficeDueTick = -1;
    private bool postOfficeFirstHandled;

    private const int PostOfficeIntervalTicks = GenDate.TicksPerDay * 15;
    private const int PostOfficeFirstCost = 500;
    private const int PostOfficeRecurringCost = 750;

    private void ExposePostOffice()
    {
        Scribe_Values.Look(ref nextPostOfficeDueTick, "nextPostOfficeDueTick", -1);
        Scribe_Values.Look(ref postOfficeFirstHandled, "postOfficeFirstHandled");
    }

    private static int CountSilverOnMap(Map map)
    {
        if (map == null)
            return 0;
        return map.listerThings.ThingsOfDef(ThingDefOf.Silver)
            .Where(t => !t.IsForbidden(Faction.OfPlayer))
            .Sum(t => t.stackCount);
    }

    private void TickPostOffice(int now)
    {
        if (stage < CampaignStage.Dreaming || leader == null || leader.Dead)
            return;

        if (nextPostOfficeDueTick < 0)
        {
            nextPostOfficeDueTick = now + PostOfficeIntervalTicks;
            return;
        }

        if (now < nextPostOfficeDueTick)
            return;

        Map map = leader.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map == null || Find.WindowStack.IsOpen(typeof(CultistStoryDialogWindow)))
            return;

        nextPostOfficeDueTick = now + PostOfficeIntervalTicks;
        if (!postOfficeFirstHandled)
            OpenFirstPostOfficeDialog(map);
        else
            OpenRecurringPostOfficeDialog(map);
    }

    private void OpenFirstPostOfficeDialog(Map map)
    {
        postOfficeFirstHandled = true;
        int silver = CountSilverOnMap(map);

        var billNode = new StoryDialogNode
        {
            NodeId = "postoffice_first",
            Title = "Cultist.PostOffice.FirstTitle".Translate(),
            SpeakerName = "Cultist.PostOffice.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.PostOffice.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Location/locationauctionhouse",
            ProfileTexPath = CultistDialogBg.Oriflamme,
            AccentColor = new Color(0.88f, 0.72f, 0.34f),
            BodyText = "Cultist.PostOffice.FirstBody".Translate(PostOfficeFirstCost, silver),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.PostOffice.OptPay".Translate(PostOfficeFirstCost),
                    Tag = "Cultist.PostOffice.TagPay".Translate(PostOfficeFirstCost),
                    Disabled = silver < PostOfficeFirstCost,
                    DisabledReason = "Cultist.PostOffice.NeedSilver".Translate(PostOfficeFirstCost),
                    OnSelect = () =>
                    {
                        if (TryConsumeSilver(map, PostOfficeFirstCost))
                        {
                            ClearUpkeepArrears();
                            PlayCultistSound("Cultist_Sfx_RiteComplete");
                            Messages.Message("Cultist.PostOffice.Paid".Translate(PostOfficeFirstCost),
                                leader, MessageTypeDefOf.PositiveEvent);
                        }
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.PostOffice.OptDeferFirst".Translate(),
                    Tag = "Cultist.PostOffice.TagDeferFirst".Translate(),
                    NextNodeId = "postoffice_poppy_paid",
                    CloseAfterSelect = false
                }
            }
        };

        var poppyNode = new StoryDialogNode
        {
            NodeId = "postoffice_poppy_paid",
            Title = "Cultist.PostOffice.PoppyTitle".Translate(),
            SpeakerName = "Cultist.PostOffice.PoppySpeaker".Translate(),
            SpeakerSubtitle = "Cultist.PostOffice.PoppySub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Patron/poppy",
            ProfileTexPath = CultistDialogBg.Poppy,
            AccentColor = new Color(0.84f, 0.28f, 0.36f),
            BodyText = "Cultist.PostOffice.PoppyBody".Translate(leader?.LabelShortCap ?? "Cultist.ChurchName".Translate()),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.PostOffice.PoppyAck".Translate(),
                    OnSelect = () =>
                    {
                        PlayCultistSound("Cultist_Sfx_RiteComplete");
                        Messages.Message("Cultist.PostOffice.PoppySettledMsg".Translate(),
                            leader, MessageTypeDefOf.NeutralEvent);
                    },
                    CloseAfterSelect = true
                }
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new[] { billNode, poppyNode }, "postoffice_first"));
    }

    private void OpenRecurringPostOfficeDialog(Map map)
    {
        int silver = CountSilverOnMap(map);

        var node = new StoryDialogNode
        {
            NodeId = "postoffice_recurring",
            Title = "Cultist.PostOffice.RecurringTitle".Translate(),
            SpeakerName = "Cultist.PostOffice.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.PostOffice.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Location/locationauctionhouse",
            ProfileTexPath = CultistDialogBg.Oriflamme,
            AccentColor = new Color(0.88f, 0.72f, 0.34f),
            BodyText = "Cultist.PostOffice.RecurringBody".Translate(PostOfficeRecurringCost, silver),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.PostOffice.OptPay".Translate(PostOfficeRecurringCost),
                    Tag = "Cultist.PostOffice.TagPay".Translate(PostOfficeRecurringCost),
                    Disabled = silver < PostOfficeRecurringCost,
                    DisabledReason = "Cultist.PostOffice.NeedSilver".Translate(PostOfficeRecurringCost),
                    OnSelect = () =>
                    {
                        if (TryConsumeSilver(map, PostOfficeRecurringCost))
                        {
                            ClearUpkeepArrears();
                            PlayCultistSound("Cultist_Sfx_RiteComplete");
                            Messages.Message("Cultist.PostOffice.Paid".Translate(PostOfficeRecurringCost),
                                leader, MessageTypeDefOf.PositiveEvent);
                        }
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.PostOffice.OptDefault".Translate(),
                    Tag = "Cultist.PostOffice.TagDefault".Translate(),
                    OnSelect = ApplyUpkeepArrears,
                    CloseAfterSelect = true
                }
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(node));
    }

    private void ApplyUpkeepArrears()
    {
        if (leader == null || leader.Dead)
            return;

        if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_UpkeepArrears") is { } hediffDef
            && leader.health != null)
        {
            Hediff existing = leader.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
                leader.health.RemoveHediff(existing);
            leader.health.AddHediff(hediffDef);
        }

        if (DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_UpkeepArrears") is { } thoughtDef)
            leader.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);

        Messages.Message("Cultist.PostOffice.DefaultedMsg".Translate(leader.LabelShortCap),
            leader, MessageTypeDefOf.NegativeEvent);
    }

    private void ClearUpkeepArrears()
    {
        if (leader == null || leader.Dead)
            return;

        if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_UpkeepArrears") is { } hediffDef
            && leader.health?.hediffSet?.GetFirstHediffOfDef(hediffDef) is { } existing)
        {
            leader.health.RemoveHediff(existing);
        }

        if (DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_UpkeepArrears") is { } thoughtDef)
            leader.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(thoughtDef);
    }
}
