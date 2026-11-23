-- Read only: run separately after the reviewed migration. No name-based updates.
SELECT i.IngredientId, i.Name, i.AllergenId AS LegacyAllergenId,
       i.IsAllergenProfileVerified, i.AllergenEvidence,
       GROUP_CONCAT(ia.AllergenId ORDER BY ia.AllergenId) AS AllergenIds
FROM Ingredients i LEFT JOIN IngredientAllergens ia ON ia.IngredientId=i.IngredientId
GROUP BY i.IngredientId, i.Name, i.AllergenId, i.IsAllergenProfileVerified, i.AllergenEvidence
ORDER BY i.IsAllergenProfileVerified, i.IngredientId;
