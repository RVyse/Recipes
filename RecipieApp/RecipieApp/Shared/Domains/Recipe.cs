using RecipieApp.Shared.Enums;

namespace RecipieApp.Shared.Domains;

public class Recipe
{
    public string Title { get; set; } = string.Empty;
    public FoodCategory FoodCategory { get; set; } = FoodCategory.Starter;
    public string Description { get; set; } = string.Empty;
    public string Ingredients { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Variations { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    // Additional properties to support filtering and metadata
    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; }
    // Comma separated tags (e.g. "vegetarian,gluten-free")
    public string Tags { get; set; } = string.Empty;

    // Convenience property
    public int TotalTimeMinutes => PrepTimeMinutes + CookTimeMinutes;

}
