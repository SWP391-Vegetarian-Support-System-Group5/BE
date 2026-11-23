using DAL.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace BLL.Services;

// Compatibility adapter: ingredient safety is never inferred from names or a model response.
public sealed class GeminiAllergenIngredientMatcher : IAllergenIngredientMatcher
{
    public GeminiAllergenIngredientMatcher(HttpClient client, GeminiChatbotSettings settings, IMemoryCache cache) { }
    public Task<IReadOnlySet<int>> FindUnsafeAsync(IReadOnlyCollection<Allergen> allergens, IReadOnlyCollection<Ingredient> ingredients, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = allergens.Select(x => x.AllergenId).ToHashSet();
        IReadOnlySet<int> blocked = ingredients.Where(i => ids.Count > 0 && (
            (i.AllergenId.HasValue && ids.Contains(i.AllergenId.Value)) || i.AllergenLinks.Any(a => ids.Contains(a.AllergenId))))
            .Select(i => i.IngredientId).ToHashSet();
        return Task.FromResult(blocked);
    }
}
