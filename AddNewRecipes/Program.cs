using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AddNewRecipes
{
    public class Program
    {
        private static String[] potionWordsA = { "Fortify", "Regenerate", "Resist", "Restore", "Waterbreathing", "Invisibility" };
        private static String[] poisonWordsA = { "Damage", "Ravage", "Fear", "Slow", "Paralyze", "Weakness" };
        private static HashSet<String> potionWords = new HashSet<String>(potionWordsA);
        private static HashSet<String> poisonWords = new HashSet<String>(poisonWordsA);
        private static String[] SkipPlugins = { "bsassets", "bsheartland", "bs_dlc_patch", "bs_Campfire", "beyond skyrim", "bruma" }; //mods containing these names will be skipped(IF ADDING KEEP LOWERCASE)
        private static String[] SkipIngrs = { "Jarrin" }; //ingredients containing these words will be skipped
        private static int impureSkipThreshold = 2; // impure potions with this or less number of effects will be skipped
        private static int potionSkipThreshold = 1; // potions with this or less number of effects will be skipped
        private static int poisonSkipThreshold = 1; // potions with this or less number of effects will be skipped
        private static float recipeWeight = 0f;
        private static int minChance = 5;//min chance(5%) of receiving a recipe
        private static int maxChance = 25;//max chance(25%) of receiving a recipe
        private static String[] containerEditorIDsA = { "TreasBanditChest", "TreasDraugrChest" }; //add containers to add the potential loot of recipe
        private static HashSet<String> containerEditorIDs = new HashSet<String>(containerEditorIDsA);
        private static double outputPercentage = 0.05; //How often to update output
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .Run(args, new RunPreferences()
                {
                    ActionsForEmptyArgs = new RunDefaultPatcher()
                    {
                        IdentifyingModKey = "AddNewRecipes.esp",
                        TargetRelease = GameRelease.SkyrimSE,
                    }
                });
        }
        //public static readonly ModKey PatchRecipeesp = new ModKey("AddNewRecipes", ModType.Plugin);
        private static uint potionRecipeCount, poisonRecipeCount, impurepotionRecipeCount;
        private static Stopwatch sw = new Stopwatch();
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            List<IngrCombination> combinations = new List<IngrCombination>();
            var ingredients = state.LoadOrder.PriorityOrder.OnlyEnabled().Ingredient().WinningOverrides()
                .Where(x => !SkipPlugins.Contains(x.FormKey.ModKey.Name.ToLower()))
                .Where(x => (!SkipIngrs.Intersect(x.Name?.ToString()?.Split()!).Any() || SkipIngrs.Contains(x.Name?.ToString())))
                .Where(x => !String.IsNullOrEmpty(x.Name?.String))
                .ToList();
            int i = 0;
            int percent = (int)(ingredients.Count * outputPercentage);
            foreach (var target in ingredients)
            {
                sw.Start();
                if (i % percent == 0)
                {
                    Console.WriteLine(i + " out of " + ingredients.Count + " ingredients processed.");
                }
                List<IIngredientGetter> remainingingr = ingredients.GetRange(i, ingredients.Count - i);
                IIngredientGetter[] potionRecipeList = getIngredientsMatchingOneIngredient(target, remainingingr);
                if (String.IsNullOrEmpty(target.Name?.String))
                {
                    i++;
                    continue;
                }
                foreach (IIngredientGetter ingr in potionRecipeList)
                {
                    var activeEffects = ingr.Effects
                        .Distinct()
                        .ToArray();
                    if (activeEffects.Length < 1)
                        continue;
                    String potionString = "<font face='$HandwrittenFont'><font size='26'>";
                    potionString += "-<b>" + (target.Name + "<br><b>-<b>" + ingr.Name + "</b>");
                    List<String?> mgeflist = new List<String?>();
                    List<String?> mgeflists = new List<String?>();
                    foreach (IEffectGetter effect in activeEffects)
                    {
                        state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgeffect);
                        mgeflist.Add(mgeffect?.Name?.String);
                        mgeflists.AddRange(mgeffect?.Name?.String?.Split()!);
                    }
                    String prefix = "Potion";
                    int type = 0;
                    if (!mgeflists.Intersect(potionWords.ToList()).Any() && mgeflists.Intersect(poisonWords.ToList()).Any())
                    {
                        prefix = "Poison";
                        type = 1;
                        if (mgeflist.Count <= poisonSkipThreshold)
                            continue;
                        poisonRecipeCount++;
                    }
                    else if (mgeflists.Intersect(potionWords.ToList()).Any() && mgeflists.Intersect(poisonWords.ToList()).Any())
                    {
                        prefix = "Impure Potion";
                        type = 2;
                        if (mgeflist.Count <= impureSkipThreshold)
                            continue;
                        impurepotionRecipeCount++;
                    }
                    else
                    {
                        if (mgeflist.Count <= potionSkipThreshold)
                            continue;
                        potionRecipeCount++;
                    }
                    potionString += ("</font><font face='$HandwrittenFont'><font size='18'><br> to make " + prefix + " of ");
                    String potionName = "Recipe: ";
                    for (int k = 0; k < mgeflist.Count; k++)
                    {
                        if (k > 0)
                        {
                            potionName += " and ";
                            potionString += ("<br>");
                        }
                        potionName += mgeflist[k];
                        potionString += (mgeflist[k]);
                    }
                    String sstring = "";

                    if (mgeflist.Count > 1)
                        sstring = "s";

                    potionString += ("<br></font><font size='14'> Contains " + mgeflist.Count + " Effect" + sstring);
                    potionString += "<\\font>";
                    IIngredientGetter[] ingrss = { target, ingr };
                    combinations.Add(new IngrCombination(potionName, ingrss, mgeflist?.ToArray()!, potionString, type));
                }
                int j = i + 1;
                foreach (var remainingIngr in remainingingr)
                {
                    if (remainingIngr.Name?.Equals(target.Name) ?? true || String.IsNullOrEmpty(remainingIngr.Name?.String) || !target.Effects.Intersect(remainingIngr.Effects).Any())
                    {
                        j++;
                        continue;
                    }
                    List<IIngredientGetter> remainingingr2 = ingredients.GetRange(j, ingredients.Count - j);
                    IIngredientGetter[] potionRecipeList2 = getIngredientsMatchingTwoIngredients(target, remainingIngr, remainingingr2);
                    foreach (IIngredientGetter ingr in potionRecipeList2)
                    {
                        IEnumerable<IEffectGetter> ActiveEffects = ingr.Effects.Intersect(target.Effects);
                        IEnumerable<IEffectGetter> ActiveEffects2 = ingr.Effects.Intersect(remainingIngr.Effects);
                        IEnumerable<IEffectGetter> ActiveEffects3 = target.Effects.Intersect(remainingIngr.Effects);
                        ActiveEffects.ToList().AddRange(ActiveEffects2);
                        ActiveEffects.ToList().AddRange(ActiveEffects3);
                        ActiveEffects = ActiveEffects.Distinct();
                        IEffectGetter[] ActiveEffectsA = ActiveEffects.ToArray();
                        if (ActiveEffectsA.Length < 1)
                            continue;
                        String potionString = "<font face='$HandwrittenFont'><font size='26'>";
                        potionString += "-<b>" + (target.Name + "<br></b>-<b>" + remainingIngr.Name + "<br></b>-<b>" + ingr.Name + "</b>");
                        List<String?> mgeflist = new List<String?>();
                        List<String?> mgeflists = new List<String?>();
                        foreach (IEffectGetter effect in ActiveEffects)
                        {
                            state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgeffect);
                            mgeflist.Add(mgeffect?.Name?.String);
                            mgeflists.AddRange(mgeffect?.Name?.String?.Split()!);
                        }
                        String prefix = "Potion";
                        int type = 0;
                        if (!mgeflists.Intersect(potionWords.ToList()).Any() && mgeflists.Intersect(poisonWords.ToList()).Any())
                        {
                            prefix = "Poison";
                            type = 1;
                            if (mgeflist.Count <= poisonSkipThreshold)
                                continue;
                            poisonRecipeCount++;
                        }
                        else if (mgeflist.Intersect(potionWords.ToList()).Any() && mgeflists.Intersect(poisonWords.ToList()).Any())
                        {
                            prefix = "Impure Potion";
                            type = 2;
                            if (mgeflists.Count <= impureSkipThreshold)
                                continue;
                            impurepotionRecipeCount++;
                        }
                        else
                        {
                            if (mgeflist.Count <= potionSkipThreshold)
                                continue;
                            potionRecipeCount++;
                        }
                        potionString += ("</font><font face='$HandwrittenFont'><font size='18'><br> to make " + prefix + " of: <br></font><font face='$HandwrittenFont'><font size='26'>");
                        String potionName = "Recipe: ";
                        for (int k = 0; k < mgeflist.Count; k++)
                        {
                            if (k > 0)
                            {
                                potionName += " and ";
                                potionString += ("<br>");
                            }
                            potionName += mgeflist[k];
                            potionString += (mgeflist[k]);
                        }
                        String sstring = "";

                        if (mgeflist.Count > 1)
                            sstring = "s";
                        potionString += ("<br></font><font size='14'> Contains " + mgeflist.Count + " Effect" + sstring);
                        potionString += "<\\font>";
                        IIngredientGetter[] ingrss = { target, remainingIngr, ingr };
                        combinations.Add(new IngrCombination(potionName, ingrss, mgeflist?.ToArray()!, potionString, type));
                    }
                    j++;
                }
                i++;
                if (i % percent == 0)
                {
                    sw.Stop();
                    Console.WriteLine("time elapsed:  " + sw.Elapsed.TotalSeconds + " seconds");
                    sw.Reset();
                }
            }
            Console.WriteLine("Creating Leveled lists...");
            IEnumerable<IBookGetter> books = from book in state.LoadOrder.PriorityOrder.Book().WinningOverrides() where book.FormKey.Equals(new FormKey(new ModKey("Skyrim", ModType.Master), 0x0F5CB1)) select book;
            IBookGetter noteTemplate = books.ToList()[0];
            Console.WriteLine("Creating " + combinations.Count + " recipes.");
            percent = (int)(combinations.Count * outputPercentage);
            i = 0;
            /* Main leveled list that gets added to recipe drop */
            LeveledItem mainpotionRecipeLVLI = state.PatchMod.LeveledItems.AddNew();
            LeveledItemEntry mainpotionRecipeLVLIentry = new LeveledItemEntry();
            mainpotionRecipeLVLI.Entries = new Noggog.ExtendedList<LeveledItemEntry>();
            LeveledItemEntryData mainpotionRecipeLVLIentrydata = new LeveledItemEntryData();
            GlobalInt mainpotionGlobal = new GlobalInt(state.PatchMod.GetNextFormKey(), SkyrimRelease.SkyrimSE);
            mainpotionGlobal.Data = new Random().Next(minChance, maxChance);
            state.PatchMod.Globals.Set(mainpotionGlobal);
            mainpotionRecipeLVLI.Global = mainpotionGlobal;
            mainpotionRecipeLVLI.EditorID = "mainpotionRecipeList";
            /* Must split sub leveled lists because it can only hold 128 items */
            uint potionRecipeListCount = (potionRecipeCount / 128) + 1;
            uint poisonRecipeListCount = (poisonRecipeCount / 128) + 1;
            uint impurepotionRecipeListCount = (impurepotionRecipeCount / 128) + 1;
            LeveledItem[] potionRecipeLVLIs = new LeveledItem[potionRecipeListCount];
            uint masterpotionRecipeListCount = ((potionRecipeListCount + poisonRecipeListCount + impurepotionRecipeListCount) / 128) + 1;
            LeveledItem[] masterpotionRecipeLVLIs = new LeveledItem[masterpotionRecipeListCount];
            LeveledItemEntry[] masterpotionRecipeLVLIentries = new LeveledItemEntry[masterpotionRecipeListCount];
            LeveledItemEntryData[] masterpotionRecipeLVLIentriesdata = new LeveledItemEntryData[masterpotionRecipeListCount];
            GlobalInt[] masterpotionGlobals = new GlobalInt[masterpotionRecipeListCount];
            LeveledItemEntry[] potionRecipeLVLIentries = new LeveledItemEntry[potionRecipeListCount];
            LeveledItemEntryData[] potionRecipeLVLIentriesdata = new LeveledItemEntryData[potionRecipeListCount];
            GlobalInt[] potionGlobals = new GlobalInt[potionRecipeListCount];
            for (int k = 0; k < masterpotionRecipeListCount; k++)
            {
                masterpotionRecipeLVLIentries[k] = new LeveledItemEntry();
                masterpotionRecipeLVLIentriesdata[k] = new LeveledItemEntryData();
                masterpotionRecipeLVLIs[k] = state.PatchMod.LeveledItems.AddNew();
                masterpotionRecipeLVLIs[k].Entries = new Noggog.ExtendedList<LeveledItemEntry>();
                masterpotionGlobals[k] = new GlobalInt(state.PatchMod.GetNextFormKey(), SkyrimRelease.SkyrimSE);
                masterpotionGlobals[k].Data = new Random().Next(5, 25);
                state.PatchMod.Globals.Set(masterpotionGlobals[k]);
                masterpotionRecipeLVLIs[k].Global = masterpotionGlobals[k];
                masterpotionRecipeLVLIs[k].EditorID = "masterpotionRecipeList" + k;
                masterpotionRecipeLVLIentriesdata[k].Reference = masterpotionRecipeLVLIs[k].FormKey;
                masterpotionRecipeLVLIentriesdata[k].Level = 1;
                masterpotionRecipeLVLIentriesdata[k].Count = 1;
            }
            for (int l = 0; l < potionRecipeListCount; l++)
            {
                potionRecipeLVLIentries[l] = new LeveledItemEntry();
                potionRecipeLVLIentriesdata[l] = new LeveledItemEntryData();
                potionRecipeLVLIs[l] = state.PatchMod.LeveledItems.AddNew();
                potionRecipeLVLIs[l].Entries = new Noggog.ExtendedList<LeveledItemEntry>();
                potionGlobals[l] = new GlobalInt(state.PatchMod.GetNextFormKey(), SkyrimRelease.SkyrimSE);
                potionGlobals[l].Data = new Random().Next(5, 25);
                state.PatchMod.Globals.Set(potionGlobals[l]);
                potionRecipeLVLIs[i].Global = potionGlobals[l];
                potionRecipeLVLIs[l].EditorID = "potionRecipeList" + l;
                potionRecipeLVLIentriesdata[l].Reference = potionRecipeLVLIs[l].FormKey;
                potionRecipeLVLIentriesdata[l].Level = 1;
                potionRecipeLVLIentriesdata[l].Count = 1;
            }
            LeveledItem[] poisonRecipeLVLIs = new LeveledItem[poisonRecipeListCount];
            LeveledItemEntry[] poisonRecipeLVLIentries = new LeveledItemEntry[poisonRecipeListCount];
            LeveledItemEntryData[] poisonRecipeLVLIentriesdata = new LeveledItemEntryData[poisonRecipeListCount];
            GlobalInt[] poisonGlobals = new GlobalInt[poisonRecipeListCount];
            for (int l = 0; l < poisonRecipeListCount; l++)
            {
                poisonRecipeLVLIentries[l] = new LeveledItemEntry();
                poisonRecipeLVLIentriesdata[l] = new LeveledItemEntryData();
                poisonRecipeLVLIs[l] = state.PatchMod.LeveledItems.AddNew();
                poisonRecipeLVLIs[l].Entries = new Noggog.ExtendedList<LeveledItemEntry>();
                poisonGlobals[l] = new GlobalInt(state.PatchMod.GetNextFormKey(), SkyrimRelease.SkyrimSE);
                poisonGlobals[l].Data = new Random().Next(5, 25);
                state.PatchMod.Globals.Set(poisonGlobals[l]);
                poisonRecipeLVLIs[i].Global = poisonGlobals[l];
                poisonRecipeLVLIs[l].EditorID = "poisonRecipeList" + l;
                poisonRecipeLVLIentriesdata[l].Reference = poisonRecipeLVLIs[l].FormKey;
                poisonRecipeLVLIentriesdata[l].Level = 1;
                poisonRecipeLVLIentriesdata[l].Count = 1;
            }
            LeveledItem[] impurepotionRecipeLVLIs = new LeveledItem[impurepotionRecipeListCount];
            LeveledItemEntry[] impurepotionRecipeLVLIentries = new LeveledItemEntry[impurepotionRecipeListCount];
            LeveledItemEntryData[] impurepotionRecipeLVLIentriesdata = new LeveledItemEntryData[impurepotionRecipeListCount];
            GlobalInt[] impurepotionGlobals = new GlobalInt[impurepotionRecipeListCount];
            for (int l = 0; l < impurepotionRecipeListCount; l++)
            {
                impurepotionRecipeLVLIentries[l] = new LeveledItemEntry();
                impurepotionRecipeLVLIentriesdata[l] = new LeveledItemEntryData();
                impurepotionRecipeLVLIs[l] = state.PatchMod.LeveledItems.AddNew();
                impurepotionRecipeLVLIs[l].Entries = new Noggog.ExtendedList<LeveledItemEntry>();
                impurepotionGlobals[l] = new GlobalInt(state.PatchMod.GetNextFormKey(), SkyrimRelease.SkyrimSE);
                impurepotionGlobals[l].Data = new Random().Next(5, 25);
                state.PatchMod.Globals.Set(impurepotionGlobals[l]);
                impurepotionRecipeLVLIs[i].Global = impurepotionGlobals[l];
                impurepotionRecipeLVLIs[l].EditorID = "impurepotionRecipeList" + l;
                impurepotionRecipeLVLIentriesdata[l].Reference = impurepotionRecipeLVLIs[l].FormKey;
                impurepotionRecipeLVLIentriesdata[l].Level = 1;
                impurepotionRecipeLVLIentriesdata[l].Count = 1;
            }
            Console.WriteLine("Splitting potions into lists (" + potionRecipeListCount + " " + poisonRecipeListCount + " " + impurepotionRecipeListCount + ")");
            uint potionIndex = 0, poisonIndex = 0, impurepotionIndex = 0;
            IEffectGetter[] effectCache = getAllEffects(ingredients).ToArray();
            Dictionary<String, int> nameCache = new Dictionary<String, int>();
            foreach (IngrCombination ic in combinations)
            {
                if (i % percent == 0)
                    Console.WriteLine(i + " out of " + combinations.Count + " recipes created.");
                IBook newRecipe = noteTemplate.DeepCopy();
                newRecipe.FormKey = state.PatchMod.GetNextFormKey();
                newRecipe.Description = ic.RecipeName;
                newRecipe.Name = ic.RecipeName;
                newRecipe.BookText = ic.PotionString;
                newRecipe.Weight = recipeWeight;
                String? name = "recipeof";
                foreach (String? s in ic.MyEffects!)
                    name += s;
                name = name.Replace(" ", String.Empty);
                int nameIndex = 0;
                if (nameCache.TryGetValue(name, out nameIndex))
                {
                    nameCache[name] = nameIndex + 1;
                    name = name + nameCache[name];
                }
                else
                {
                    nameCache.Add(name, 0);
                    name = name + "0";
                }
                newRecipe.EditorID = name;
                state.PatchMod.Books.Set((Book)newRecipe);
                LeveledItemEntry lie = new LeveledItemEntry();
                LeveledItemEntryData data = new LeveledItemEntryData();
                data.Level = 1;
                data.Count = 1;
                data.Reference = new FormLink<IItemGetter>(newRecipe.FormKey);
                lie.Data = data;
                switch (ic.Type)
                {

                    case 0:
                        potionRecipeLVLIentriesdata[potionIndex / 128].Reference = potionRecipeLVLIs[potionIndex / 128].FormKey;
                        potionRecipeLVLIentries[potionIndex / 128].Data = potionRecipeLVLIentriesdata[potionIndex / 128];
                        potionRecipeLVLIs[potionIndex / 128].Entries?.Add(lie);
                        potionIndex++;
                        break;
                    case 1:
                        poisonRecipeLVLIentriesdata[poisonIndex / 128].Reference = poisonRecipeLVLIs[poisonIndex / 128].FormKey;
                        poisonRecipeLVLIentries[poisonIndex / 128].Data = poisonRecipeLVLIentriesdata[poisonIndex / 128];
                        poisonRecipeLVLIs[poisonIndex / 128].Entries?.Add(lie);
                        poisonIndex++;
                        break;
                    case 2:
                        impurepotionRecipeLVLIentriesdata[impurepotionIndex / 128].Reference = impurepotionRecipeLVLIs[impurepotionIndex / 128].FormKey;
                        impurepotionRecipeLVLIentries[impurepotionIndex / 128].Data = impurepotionRecipeLVLIentriesdata[impurepotionIndex / 128];
                        impurepotionRecipeLVLIs[impurepotionIndex / 128].Entries?.Add(lie);
                        impurepotionIndex++;
                        break;
                }
                i++;
            }

            Console.WriteLine("Linking recipes to potion leveled list");
            IEnumerable<ILeveledItemGetter> lvlilists = from list in state.LoadOrder.PriorityOrder.OnlyEnabled().LeveledItem().WinningOverrides() where list.EditorID?.Equals("LItemPotionAll") ?? true select list;
            ILeveledItemGetter allList = lvlilists.ToList()[0];
            LeveledItem modifiedList = state.PatchMod.LeveledItems.GetOrAddAsOverride(allList);
            potionIndex = 0;
            poisonIndex = 0;
            impurepotionIndex = 0;
            for (int l = 0; l < masterpotionRecipeListCount; l++)
            {
                masterpotionRecipeLVLIentriesdata[l].Reference = masterpotionRecipeLVLIs[l].FormKey;
                masterpotionRecipeLVLIentries[l].Data = masterpotionRecipeLVLIentriesdata[l];
                for (int k = 0; k < 128; k++)
                {
                    if (potionIndex < potionRecipeLVLIentries.Length)
                        masterpotionRecipeLVLIs[l].Entries?.Add(potionRecipeLVLIentries[potionIndex++]);
                    else if (poisonIndex < poisonRecipeLVLIentries.Length)
                        masterpotionRecipeLVLIs[l].Entries?.Add(poisonRecipeLVLIentries[poisonIndex++]);
                    else if (impurepotionIndex < impurepotionRecipeLVLIentries.Length)
                        masterpotionRecipeLVLIs[l].Entries?.Add(impurepotionRecipeLVLIentries[impurepotionIndex++]);
                    else
                        break;
                }
                mainpotionRecipeLVLI.Entries?.Add(masterpotionRecipeLVLIentries[l]);
            }
            foreach (LeveledItem li in potionRecipeLVLIs)
                state.PatchMod.LeveledItems.Set(li);
            foreach (LeveledItem li in poisonRecipeLVLIs)
                state.PatchMod.LeveledItems.Set(li);
            foreach (LeveledItem li in impurepotionRecipeLVLIs)
                state.PatchMod.LeveledItems.Set(li);
            foreach (LeveledItem li in masterpotionRecipeLVLIs)
                state.PatchMod.LeveledItems.Set(li);

            mainpotionRecipeLVLIentrydata.Reference = mainpotionRecipeLVLI.FormKey;
            mainpotionRecipeLVLIentry.Data = mainpotionRecipeLVLIentrydata;
            mainpotionRecipeLVLIentrydata.Count = 1;
            mainpotionRecipeLVLIentrydata.Level = 1;
            modifiedList.Entries?.Add(mainpotionRecipeLVLIentry);
            state.PatchMod.LeveledItems.Set(mainpotionRecipeLVLI);
            Console.WriteLine("Adding recipes to defined containers");
            IEnumerable<IContainerGetter> chests = from list in state.LoadOrder.PriorityOrder.OnlyEnabled().Container().WinningOverrides() where containerEditorIDs?.ToList().Contains(list.EditorID!) ?? true select list;
            ContainerEntry potionListContainerEntry = new ContainerEntry();
            ContainerItem potionListContainerItem = new ContainerItem();
            potionListContainerItem.Item = mainpotionRecipeLVLI.FormKey;
            potionListContainerItem.Count = 1;
            potionListContainerEntry.Item = potionListContainerItem;
            foreach(IContainerGetter chest in chests)
            {
                Container rChest = state.PatchMod.Containers.GetOrAddAsOverride(chest);
                rChest.Items?.Add(potionListContainerEntry);
            }
        }
        private static IIngredientGetter[] getIngredientsMatchingOneIngredient(IIngredientGetter firstIngredient, IEnumerable<IIngredientGetter> otherIngredients)
        {
            List<IEffectGetter> firstIngredientEffects = firstIngredient.Effects.ToList();
            return (from matchingEffects in otherIngredients.ToList() where (firstIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any()) && (!firstIngredient.IngredientValue.Equals(matchingEffects.IngredientValue)) select matchingEffects).ToArray();

        }
        private static IIngredientGetter[] getIngredientsMatchingTwoIngredients(IIngredientGetter firstIngredient, IIngredientGetter secondIngredient, IEnumerable<IIngredientGetter> otherIngredients)
        {
            List<IEffectGetter> firstIngredientEffects = firstIngredient.Effects.ToList();
            List<IEffectGetter> secondIngredientEffects = secondIngredient.Effects.ToList();
            return (from matchingEffects in otherIngredients.ToList() where ((firstIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any()) || (secondIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any())) && (!firstIngredient.IngredientValue.Equals(matchingEffects.IngredientValue) && !secondIngredient.IngredientValue.Equals(matchingEffects.IngredientValue) && !firstIngredient.IngredientValue.Equals(secondIngredient.IngredientValue)) select matchingEffects).ToArray();
        }
        private static IEnumerable<IEffectGetter> getAllEffects(IEnumerable<IIngredientGetter> ingrs)
        {
            IEnumerable<IEffectGetter> effects = Enumerable.Empty<IEffectGetter>();
            foreach (var ingr in ingrs)
                foreach (var effect in ingr.Effects)
                    effects.ToList().Add(effect);
            effects = effects.Distinct();
            return effects;
        }
    }
    class IngrCombination
    {
        string? recipeName;
        IIngredientGetter[] myIngrs;
        string[]? myEffects;
        string? potionString;
        int type = 0;
        public IngrCombination(String? name, IIngredientGetter[] ingrs, String[]? effects, String? pstring, int type)
        {
            this.recipeName = name;
            this.myIngrs = ingrs;
            this.myEffects = effects;
            this.potionString = pstring;
            this.type = type;
        }
        public IIngredientGetter[] MyIngrs { get => myIngrs; set => myIngrs = value; }
        public String[]? MyEffects { get => myEffects; set => myEffects = value; }
        public string? PotionString { get => potionString; set => potionString = value; }
        public string? RecipeName { get => recipeName; set => recipeName = value; }
        public int Type { get => type; set => type = value; }
    }
}