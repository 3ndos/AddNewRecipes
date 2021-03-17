using System;
using System.Collections.Generic;

namespace AddNewRecipes
{
    public class Settings
    {
        public List<String> SkipPlugins = new List<string> { "bsassets", "bsheartland", "bs_dlc_patch", "bs_Campfire", "beyond skyrim", "bruma" };
        public List<String> SkipIngrs = new List<String> { "Jarrin" } ;
        public int impureSkipThreshold = 2;
        public int PotionSkipThreshold = 1;
        public int PoisonSkipThreshold = 1;
        public float recipeWeight = 0f;
        public uint recipeValue = 250;
        public bool learnEffectsFromRecipe = true;
        public bool HasValueAfterRead = true;
        public int minChance = 5;
        public int maxChance = 25;
        public List<String> containerEditorIDsA = new List<string> { "TreasBanditChest", "TreasDraugrChest" };
        public double outputPercentage = 0.05;
        public int workerThreadCount = 4;
    }
}
