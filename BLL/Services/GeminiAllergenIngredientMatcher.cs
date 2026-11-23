using DAL.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace BLL.Services;

// Compatibility adapter sharing deterministic relation and keyword exclusion rules.
public sealed class GeminiAllergenIngredientMatcher : IAllergenIngredientMatcher
{
    public GeminiAllergenIngredientMatcher(HttpClient client, GeminiChatbotSettings settings, IMemoryCache cache) { }
    public Task<IReadOnlySet<int>> FindUnsafeAsync(IReadOnlyCollection<Allergen> allergens, IReadOnlyCollection<Ingredient> ingredients, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = allergens.Select(x => x.AllergenId).ToHashSet();
        IReadOnlySet<int> blocked = ingredients.Where(i => ids.Count > 0 && (
            AllergenKeywordRules.Matches(allergens.Select(a => a.Name), i.Name, i.IsAllergenProfileVerified) ||
            (i.AllergenId.HasValue && ids.Contains(i.AllergenId.Value)) || i.AllergenLinks.Any(a => ids.Contains(a.AllergenId))))
            .Select(i => i.IngredientId).ToHashSet();
        return Task.FromResult(blocked);
    }
}
