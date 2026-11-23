using DAL.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace BLL.Services;

// Compatibility adapter: deterministic name and recorded-link matching; no model response is needed.
public sealed class GeminiAllergenIngredientMatcher : IAllergenIngredientMatcher
{
    public GeminiAllergenIngredientMatcher(HttpClient client, GeminiChatbotSettings settings, IMemoryCache cache) { }
    public Task<IReadOnlySet<int>> FindUnsafeAsync(IReadOnlyCollection<Allergen> allergens, IReadOnlyCollection<Ingredient> ingredients, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlySet<int> blocked = ingredients.Where(i => IngredientAllergyMatch.Matches(i, allergens))
            .Select(i => i.IngredientId).ToHashSet();
        return Task.FromResult(blocked);
    }
}
