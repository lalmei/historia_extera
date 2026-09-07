using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.Biography.Rendering;

/// <summary>Turns selected interpretations into biography prose.</summary>
public static class BiographyRenderer
{
    public static string Render(
        BiographyContext context,
        IReadOnlyList<BiographyInterpretation> interpretations,
        IRng? rng = null)
    {
        if (interpretations.Count == 0) return string.Empty;

        var sentences = new List<string>(interpretations.Count);
        for (int i = 0; i < interpretations.Count; i++)
        {
            BiographyInterpretation interpretation = interpretations[i];
            int variant = VariantIndex(context.Figure.Id, interpretation.Theme, i, rng);
            sentences.Add(ThemeRenderers.Render(context, interpretation, variant));
        }

        return string.Join(" ", sentences);
    }

    private static int VariantIndex(EntityId figureId, BiographyTheme theme, int index, IRng? rng)
    {
        if (rng is not null)
        {
            IRng fork = rng.Fork("theme", (long)theme * 0x100000000L + index);
            return fork.NextInt(3);
        }

        uint mix = Narration.VariantMix;
        uint hash = ((uint)figureId.ToDiscriminator() * mix)
            + ((uint)(int)theme * 0x9E3779B9u)
            + (uint)index;
        return (int)(hash % 3u);
    }
}
