using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Cultist;

public enum CatalogCategory
{
    Book,
    Lore,
    Tool,
    Ingredient,
    Influence,
    Follower,
    Summon,
    Patron,
    Location,
    Society
}

public sealed class CatalogAspectEntry
{
    public string key;
    public int value;
}

public sealed class CultistCatalogDef : Def
{
    public string sourceId;
    public CatalogCategory category;
    public string iconPath;
    public bool unique;
    public List<CatalogAspectEntry> aspects = new();

    private static Dictionary<string, CultistCatalogDef> bySourceId;

    public int AspectValue(string key) =>
        aspects.FirstOrDefault(entry => entry.key == key)?.value ?? 0;

    public static CultistCatalogDef BySourceId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        bySourceId ??= DefDatabase<CultistCatalogDef>.AllDefsListForReading
            .Where(def => !string.IsNullOrEmpty(def.sourceId))
            .GroupBy(def => def.sourceId)
            .ToDictionary(group => group.Key, group => group.First());
        return bySourceId.TryGetValue(id, out CultistCatalogDef found) ? found : null;
    }

    public static IEnumerable<CultistCatalogDef> InCategory(CatalogCategory cat) =>
        DefDatabase<CultistCatalogDef>.AllDefsListForReading.Where(def => def.category == cat);
}
