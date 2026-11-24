using BLL.Services;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

static class AllergenKeywordTests
{
    public static async Task RunAsync()
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS keyword allergies " + label); }
        Check(IngredientAllergyMatch.NameMatches("  NẤM HƯƠNG khô ", "nam huong"), "case, accents and preparation suffix");
        Check(IngredientAllergyMatch.NameMatches("nấm đùi gà", "nấm"), "general keyword matches variety");
        Check(IngredientAllergyMatch.NameMatches("ĐẬU PHỘNG rang", "dau phong"), "Vietnamese crossed d");
        Check(!IngredientAllergyMatch.NameMatches("rau ngót", "nấm"), "unrelated ingredient retained");
        Check(!IngredientAllergyMatch.NameMatches("edamame", "me"), "whole word boundary");
        Check(!IngredientAllergyMatch.NameMatches("nấm đùi gà", "nấm hương"), "specific selection does not invent other allergies");
        Check(IngredientAllergyMatch.NameMatches("lạc rang", "Đậu phộng"), "remote peanut aliases retained");
        Check(IngredientAllergyMatch.NameMatches("shiitake khô", "nấm hương"), "specific mushroom alias retained");
        foreach (var (allergen, ingredient) in new[]
        {
            ("Đậu nành", "tàu hũ ky"), ("Soy", "DAU PHU NON"), ("Đậu tương", "nước tương"),
            ("Soybean", "tempeh"), ("Sữa", "bơ lạt"), ("Milk", "whey protein"), ("Trứng", "ovalbumin"),
            ("Egg", "mayonnaise"), ("Đậu phộng", "groundnuts"), ("Peanut", "bơ lạc"),
            ("Gluten", "seitan"), ("Gluten", "đại mạch"), ("Tree Nut", "hạt dẻ cười"), ("Vừng", "tahini")
        })
            Check(IngredientAllergyMatch.NameMatches(ingredient, allergen), $"{allergen} excludes {ingredient}");
        Check(!IngredientAllergyMatch.NameMatches("đậu xanh", "Đậu nành"), "soy does not exclude unrelated beans");
        Check(!IngredientAllergyMatch.NameMatches("củ lạc tiên", "Lạc"), "peanut does not exclude unrelated lạc tiên");
        Check(!IngredientAllergyMatch.NameMatches("hạt điều", "hạnh nhân"), "individual nut does not expand to the tree-nut group");
        await using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var category = new IngredientCategory { IngredientCategoryId = 1, Name = "Plants", Code = "PLANT", DietaryGroup = "PLANT" };
        var names = new[] { "Nấm hương", "nấm đùi gà", "cà rốt", "NAM HUONG kho" };
        for (var id = 1; id <= names.Length; id++)
        {
            var ingredient = new Ingredient { IngredientId = id, Name = names[id-1], Category = category };
            db.Recipes.Add(new Recipe { PostId = id, RecipeName = names[id-1],
                Post = new Post { PostId = id, Title = names[id-1], Content = "test", PostType = "RECIPE", Status = "PUBLISHED" },
                Ingredients = [new RecipeIngredient { Ingredient = ingredient, IngredientId = id, Amount = "100g" }] });
        }
        await db.SaveChangesAsync();
        var specific = new Allergen { AllergenId = 1, Name = "Nấm hương" };
        var filter = await PersonalRecipeFilter.CreateAsync(db.Ingredients, null, [specific], default);
        Check((await filter.Apply(db.Recipes).Select(r => r.PostId).ToArrayAsync()).Order().SequenceEqual(new[] { 2, 3 }), "unmapped exact ingredients excluded");
        var broad = new Allergen { AllergenId = 2, Name = "nấm" };
        filter = await PersonalRecipeFilter.CreateAsync(db.Ingredients, null, [broad], default);
        Check((await filter.Apply(db.Recipes).Select(r => r.PostId).ToArrayAsync()).SequenceEqual(new[] { 3 }), "broad keyword excludes mushroom dishes and retains carrot");
        Check(filter.Matches(await db.Recipes.SingleAsync(r => r.PostId == 3)) && !filter.Matches(await db.Recipes.SingleAsync(r => r.PostId == 1)), "in-memory and query checks agree");
        using var mysql = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseMySql("Server=localhost;Database=test;User=test;Password=test", new MySqlServerVersion(new Version(8, 0, 0))).Options);
        var sql = filter.Apply(mysql.Recipes).Take(1).ToQueryString();
        Check(sql.Contains("IngredientId") && sql.Contains("LIMIT"), "keyword filtering translates to SQL before limit");
        filter = await PersonalRecipeFilter.CreateAsync(db.Ingredients, null, [], default);
        Check(await filter.Apply(db.Recipes).CountAsync() == 4, "clearing allergy restores recipes");
        Check(await db.Ingredients.AllAsync(i => i.AllergenId == null && !i.IsAllergenProfileVerified), "matching does not rewrite catalog evidence");
    }
}
