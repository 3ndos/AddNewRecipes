using System;
using System.Collections.Generic;

namespace AddNewRecipes
{
    public class Settings
    {
        public List<String> SkipPlugins = new List<string> { "bsassets", "bsheartland", "bs_dlc_patch", "bs_Campfire", "beyond skyrim", "bruma" };
        public List<String> SkipIngredients = new List<String> { "Jarrin" } ;
        public int ImpureSkipThreshold = 2;
        public int PotionSkipThreshold = 0;
        public int PoisonSkipThreshold = 0;
        public float RecipeWeight = 0f;
        public uint RecipeValue = 250;
        public bool LearnEffectsFromRecipe = true;
        public bool HasValueAfterRead = true;
        public int MinChance = 5;
        public int MaxChance = 25;
        public List<String> ContainerEditorIds = new List<string> { "TreasBanditChest", "TreasDraugrChest" };
        public double OutputPercentage = 0.05;
        public int WorkerThreadCount = 4;
    }
}