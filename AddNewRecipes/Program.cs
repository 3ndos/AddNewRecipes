using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AddNewRecipes
{
    public class Program
    {
        private static String[] SkipPlugins = { "bsassets", "bsheartland", "bs_dlc_patch", "bs_Campfire", "beyond skyrim", "bruma" }; //mods containing these names will be skipped(IF ADDING KEEP LOWERCASE)
        private static String[] SkipIngrs = { "Jarrin" }; //ingredients containing these words will be skipped
        public static int impureSkipThreshold = 2; // impure potions with this or less number of effects will be skipped
        public static int potionSkipThreshold = 1; // potions with this or less number of effects will be skipped
        public static int poisonSkipThreshold = 1; // potions with this or less number of effects will be skipped
        private static float recipeWeight = 0f;//how much will the recipes weigh
        private static uint recipeValue = 250;//how much will the recipes be worth
        private static bool hasValueAfterRead = true;//only takes effect if Complete Alchemy and Cooking Overhaul is installed
        private static bool learnEffectsFromRecipe = true;//only takes effect if Complete Alchemy and Cooking Overhaul is installed, recommended to also install the included patch for CACO
        private static int minChance = 5;//min chance(5%) of receiving a recipe
        private static int maxChance = 25;//max chance(25%) of receiving a recipe
        private static String[] containerEditorIDsA = { "TreasBanditChest", "TreasDraugrChest" }; //add containers to add the potential loot of recipe
        public static double outputPercentage = 0.05; //How often to update output
        private static int workerThreadCount = 4;//How many threads for to process ingredients list, if you're running out of memory lower this number(will be slower but less resource intensive)
        private static HashSet<String> containerEditorIDs = new HashSet<String>(containerEditorIDsA);
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
        private static String[] badKeywords = { "MagicAlchHarmful" };
        public static List<IngrCombination> combinations = new List<IngrCombination>();
        public static Mutex ourMutex = new Mutex();
        private static int percent;
        public static bool finishedProcessing;
        public static uint potionRecipeCount, poisonRecipeCount, impurepotionRecipeCount;
        public static int reportedCount = -1, totalProcessedCount = 0, totalIngredientCount = 0;
        public static IEnumerable<IIngredientGetter>? allIngredients;
        public static Stopwatch sw = new Stopwatch();
        public static IEnumerable<IFormLink<IKeyword>>? badKeywordsF;
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {

            badKeywordsF = (from keyword in state.LoadOrder.PriorityOrder.OnlyEnabled().Keyword().WinningOverrides() where badKeywords.Contains(keyword.EditorID) select (IFormLink<IKeyword>)(new FormLink<IKeyword>(keyword.FormKey))).ToList();
            IEnumerable<IIngredientGetter> ingredients = state.LoadOrder.PriorityOrder.OnlyEnabled().Ingredient().WinningOverrides().Where(x => !SkipPlugins.Contains(x.FormKey.ModKey.Name.ToLower())).Where(x => (!SkipIngrs.Intersect(x.Name?.ToString()?.Split()!).Any() || SkipIngrs.Contains(x.Name?.ToString()))).Where(x => !String.IsNullOrEmpty(x.Name?.String)).ToList();
            allIngredients = ingredients;
            percent = (int)(ingredients.Count() * outputPercentage);
            totalIngredientCount = ingredients.Count();
            Thread[] threads = new Thread[workerThreadCount];
            int partitionsize = (ingredients.Count() / workerThreadCount);
            /* Split ingredient list evenly across threads */
            IEnumerable<IIngredientGetter>[] ingredientsL = ingredients.Partition(partitionsize).ToArray();
            /* If there is a remainder add it to last thread */
            if (ingredientsL.Length > workerThreadCount)
            {
                ingredientsL[ingredientsL.Length - 2] = ingredientsL[ingredientsL.Length - 2].Concat(ingredientsL[ingredientsL.Length - 1]);
            }
            sw.Start();
            Console.WriteLine("Using " + workerThreadCount + " threads to handle " + partitionsize + " ingredients each.");
            int startindex = 0;
            for (int u = 0; u < workerThreadCount; u++)
            {
                ListProcessor lp = new ListProcessor(u, state, ingredientsL[u], startindex);
                threads[u] = new Thread(new ThreadStart(lp.run));
                threads[u].Start();
                startindex += partitionsize;
            }
            while (!finishedProcessing)
            {
                if (totalProcessedCount % percent == 0)
                {
                    if (reportedCount != totalProcessedCount)
                    {
                        Console.WriteLine(totalProcessedCount + " out of " + ingredients.Count() + " ingredients processed.");
                        sw.Stop();
                        Console.WriteLine("time elapsed:  " + sw.Elapsed.TotalSeconds + " seconds");
                        sw.Reset();
                        sw.Start();
                        reportedCount = totalProcessedCount;
                    }
                }
                Thread.Sleep(100);
            };
            Console.WriteLine("Terminating Threads.");
            for (int u = 0; u < workerThreadCount; u++)
            {
                threads[u].Join();
            }
            Console.WriteLine("Creating Leveled lists...");
            IEnumerable<IBookGetter> books = from book in state.LoadOrder.PriorityOrder.Book().WinningOverrides() where book.FormKey.Equals(new FormKey(new ModKey("Skyrim", ModType.Master), 0x0F5CB1)) select book;
            IBookGetter noteTemplate = books.ToList()[0];
            Console.WriteLine("Creating " + combinations.Count + " recipes.");
            percent = (int)(combinations.Count * outputPercentage);
            int i = 0;
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
                masterpotionGlobals[k].Data = new Random().Next(5, 25);//Chance of picking a recipe from this list
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
                potionGlobals[l].Data = new Random().Next(5, 25);//Chance of picking a recipe from this list
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
                poisonGlobals[l].Data = new Random().Next(5, 25);//Chance of picking a recipe from this list
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
                impurepotionGlobals[l].Data = new Random().Next(5, 25);//Chance of picking a recipe from this list
                state.PatchMod.Globals.Set(impurepotionGlobals[l]);
                impurepotionRecipeLVLIs[i].Global = impurepotionGlobals[l];
                impurepotionRecipeLVLIs[l].EditorID = "impurepotionRecipeList" + l;
                impurepotionRecipeLVLIentriesdata[l].Reference = impurepotionRecipeLVLIs[l].FormKey;
                impurepotionRecipeLVLIentriesdata[l].Level = 1;
                impurepotionRecipeLVLIentriesdata[l].Count = 1;
            }
            Console.WriteLine("Splitting potions into lists (" + potionRecipeListCount + " " + poisonRecipeListCount + " " + impurepotionRecipeListCount + ")");
            uint potionIndex = 0, poisonIndex = 0, impurepotionIndex = 0;
            Dictionary<String, int> nameCache = new Dictionary<String, int>();
            foreach (IngrCombination ic in combinations)
            {
                if (i % percent == 0)
                    Console.WriteLine(i + " out of " + combinations.Count + " recipes created.");
                IBook newRecipe = noteTemplate.DeepCopy();
                newRecipe.FormKey = state.PatchMod.GetNextFormKey();
                String prefix = "[Potion]";
                if (ic.Type == 1)
                    prefix = "[Poison]";
                if (ic.Type == 2)
                    prefix = "[Impure Potion]";
                newRecipe.Description = prefix+ic.RecipeName;
                newRecipe.Name = ic.RecipeName;
                newRecipe.BookText = ic.PotionString;
                newRecipe.Weight = recipeWeight;
                newRecipe.Value = recipeValue;
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
                /* Add ingredients to CACO learning recipe script */
                if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("Complete Alchemy & Cooking Overhaul.esp")))
                {
                    String[] s = (from scriptentry in newRecipe.VirtualMachineAdapter?.Scripts where scriptentry.Name.Equals("CACO_AlchemyRecipeScript") select scriptentry.Name).ToArray();
                    if (s.Length < 1 && learnEffectsFromRecipe)//For adding recipe to a brand new item (not a copy of a vanilla recipe)
                    {
                        ScriptEntry cacoscript = new ScriptEntry();
                        cacoscript.Name = "CACO_AlchemyRecipeScript";
                        newRecipe.VirtualMachineAdapter?.Scripts.Add(cacoscript);
                    }
                    if (newRecipe.VirtualMachineAdapter?.Scripts != null)//For modiying a copy of a vanilla recipe modified by CACO(default)
                    {

                        foreach (ScriptEntry se in newRecipe.VirtualMachineAdapter?.Scripts!)
                        {
                            if (se == null)
                                continue;
                            if (se.Name.Equals("CACO_AlchemyRecipeScript"))
                            {
                                if (!learnEffectsFromRecipe)
                                {
                                    newRecipe.VirtualMachineAdapter?.Scripts?.Remove(se);
                                    continue;
                                }
                                int[,] ingrEffectIndex = new int[3, 4];
                                for (int j = 0; j < ingrEffectIndex.GetLength(0); j++)
                                    for (int k = 0; k < ingrEffectIndex.GetLength(1); k++)
                                        ingrEffectIndex[j, k] = -1;
                                for (int j = 0; j < ic.MyIngrs.Length; j++)
                                {
                                    int offset = 0;
                                    for (int k = 0; k < ic.MyIngrs[j].Effects.Count; k++)
                                    {
                                        foreach (String mgefname in ic.MyEffects)
                                        {

                                            state.LinkCache.TryResolve<IMagicEffectGetter>(ic.MyIngrs[j].Effects[k].BaseEffect.FormKey, out var mgeffect);
                                            if (mgeffect?.Name?.String?.Equals(mgefname) ?? true)
                                            {
                                                ingrEffectIndex[j, offset++] = k;
                                            }
                                        }
                                    }

                                }
                                bool[,] exists = new bool[3, 4];
                                bool[] rexists = new bool[3];
                                bool trexist = false, nvarexist = false, arrexist = false;
                                foreach (ScriptProperty sp in se.Properties)//Scan CACO learning script properties
                                {
                                    switch (sp.Name)
                                    {
                                        case "ThisRecipe":
                                        sp.Flags = ScriptProperty.Flag.Edited;
                                        ((ScriptObjectProperty)sp).Object = new FormLink<ISkyrimMajorRecordGetter>(newRecipe.FormKey);
                                        trexist = true;
                                            break;
                                        case "NoValueAfterRead":
                                            sp.Flags = ScriptProperty.Flag.Edited;
                                            ((ScriptBoolProperty)sp).Data = hasValueAfterRead;
                                            nvarexist = true;
                                            break;
                                        case "CACO_AlchemyRecipesRead":
                                            arrexist = true;
                                            break;
                                    }
                                    for (int j = 0; j < 3; j++)
                                        if (sp.Name.Equals("Ingredient0" + (j + 1)))
                                            if (ic.MyIngrs.Length > j)
                                            {
                                                sp.Flags = ScriptProperty.Flag.Edited;
                                                ((ScriptObjectProperty)sp).Object = new FormLink<ISkyrimMajorRecordGetter>(ic.MyIngrs[j].FormKey);
                                                rexists[j] = true;
                                            }
                                    for (int j = 0; j < 3; j++)
                                        for (int k = 0; k < 4; k++)
                                            if (sp.Name.Equals("Ingredient0" + (j + 1) + "Effect" + (k + 1)))
                                                if (ingrEffectIndex[j, k] != -1)
                                                {
                                                    ((ScriptIntProperty)sp).Data = ingrEffectIndex[j, k];
                                                    exists[j, k] = true;
                                                }
                                }
                                for (int j = 0; j < rexists.Length; j++)
                                    if (ic.MyIngrs.Length > j)
                                        if (!rexists[j])
                                        {
                                            ScriptObjectProperty sop = new ScriptObjectProperty();
                                            sop.Object = new FormLink<ISkyrimMajorRecordGetter>(ic.MyIngrs[j].FormKey);
                                            sop.Name = "Ingredient0" + (j + 1);
                                            sop.Flags = ScriptProperty.Flag.Edited;
                                            se.Properties.Add(sop);
                                        }
                                for (int j = 0; j < exists.GetLength(0); j++)
                                    for (int k = 0; k < exists.GetLength(1); k++)
                                    {
                                        if (ic.MyIngrs.Length > j)
                                            if (!exists[j, k] && ingrEffectIndex[j, k] != -1)
                                            {
                                                ScriptIntProperty sip = new ScriptIntProperty();
                                                sip.Data = ingrEffectIndex[j, k];
                                                sip.Name = "Ingredient0" + (j + 1) + "Effect" + (k + 1);
                                                sip.Flags = ScriptProperty.Flag.Edited;
                                                se.Properties.Add(sip);
                                            }
                                    }
                                if (!trexist)
                                {
                                    ScriptObjectProperty sop = new ScriptObjectProperty();
                                    sop.Object = new FormLink<ISkyrimMajorRecordGetter>(newRecipe.FormKey);
                                    sop.Name = "ThisRecipe";
                                    sop.Flags = ScriptProperty.Flag.Edited;
                                    se.Properties.Add(sop);
                                }
                                if(!nvarexist)
                                {
                                    ScriptBoolProperty sbp = new ScriptBoolProperty();
                                    sbp.Data = hasValueAfterRead;
                                    sbp.Name = "NoValueAfterRead";
                                    sbp.Flags = ScriptProperty.Flag.Edited;
                                    se.Properties.Add(sbp);
                                }
                                if(arrexist)
                                {
                                    FormList fl = new FormList(new FormKey(new ModKey("Complete Alchemy & Cooking Overhaul.esp", ModType.Plugin), 0xA2C667), SkyrimRelease.SkyrimSE);
                                    ScriptObjectProperty sop = new ScriptObjectProperty();
                                    sop.Object = fl;
                                    sop.Name = "CACO_AlchemyRecipesRead";
                                    sop.Flags = ScriptProperty.Flag.Edited;
                                    se.Properties.Add(sop);
                                }
                            }
                        }
                    }
                }

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
            foreach (IContainerGetter chest in chests)
            {
                Container rChest = state.PatchMod.Containers.GetOrAddAsOverride(chest);
                rChest.Items?.Add(potionListContainerEntry);
            }
        }
    }
    public class IngrCombination
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
    /* Worker Thread Class */
    class ListProcessor
    {
        int threadid, startIndex;
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state;
        IEnumerable<IIngredientGetter> ingredients;
        public ListProcessor(int threadid, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IEnumerable<IIngredientGetter> ingredients, int startIndex)
        {
            this.threadid = threadid;
            this.state = state;
            this.ingredients = ingredients;
            this.startIndex = startIndex;
        }
        public void run()
        {
            int i = 0;
            foreach (IIngredientGetter ingredient in ingredients)//loop through all ingredients
            {
                if (String.IsNullOrEmpty(ingredient.Name?.String))
                {
                    i++;
                    continue;
                }
                List<IIngredientGetter> remainingingr = Program.allIngredients!.Skip(startIndex + i).ToList();
                IIngredientGetter[] potionRecipeList = getIngredientsMatchingOneIngredient(ingredient, remainingingr);
                for (int m = 0; m < potionRecipeList.Length; m++)
                {
                    IIngredientGetter ingr = potionRecipeList[m];
                    IEnumerable<IEffectGetter> ActiveEffects = ingr.Effects.Intersect(ingredient.Effects).ToArray();
                    ActiveEffects = ActiveEffects.Distinct();
                    IEffectGetter[] ActiveEffectsA = ActiveEffects.ToArray();
                    if (ActiveEffectsA.Length < 1)
                        continue;
                    String potionString = "<font face='$HandwrittenFont'><font size='26'>";//Define main text for recipe
                    potionString += "-<b>" + (ingredient.Name + "<br><b>-<b>" + ingr.Name + "</b>");

                    List<String?> mgeflist = new List<String?>();
                    List<String?> mgeflists = new List<String?>();
                    List<bool> mgeflistD = new List<bool>();
                    for (int n = 0; n < ActiveEffectsA.Length; n++)
                    {
                        IEffectGetter effect = ActiveEffectsA[n];
                        state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgeffect);
                        mgeflist.Add(mgeffect?.Name?.String);
                        mgeflists.AddRange(mgeffect?.Name?.String?.Split()!);
                        IReadOnlyList<IFormLink<IKeywordGetter>>? thisKeywords = mgeffect.Keywords;
                        if (mgeffect.Flags.HasFlag(MagicEffect.Flag.Detrimental) || mgeffect.Flags.HasFlag(MagicEffect.Flag.Hostile) && (thisKeywords?.Intersect(Program.badKeywordsF!).Any() ?? true))
                            mgeflistD.Add(true);
                        else
                            mgeflistD.Add(false);
                    }
                    String prefix = "Potion";
                    int type = 0;
                    if (mgeflistD.All(x => x.Equals(true)))
                    {
                        prefix = "Poison";
                        type = 1;
                        if (mgeflist.Count <= Program.poisonSkipThreshold)
                            continue;
                        Program.ourMutex.WaitOne();
                        Program.poisonRecipeCount++;
                        Program.ourMutex.ReleaseMutex();
                    }
                    else if (mgeflistD.Contains(true))//Check if effects match poison and potion
                    {
                        prefix = "Impure Potion";
                        type = 2;
                        if (mgeflist.Count <= Program.impureSkipThreshold)
                            continue;
                        Program.ourMutex.WaitOne();
                        Program.impurepotionRecipeCount++;
                        Program.ourMutex.ReleaseMutex();
                    }
                    else
                    {
                        if (mgeflist.Count <= Program.potionSkipThreshold)
                            continue;
                        Program.ourMutex.WaitOne();
                        Program.potionRecipeCount++;
                        Program.ourMutex.ReleaseMutex();
                    }
                    potionString += "</font><font face='$HandwrittenFont'><font size='26'><br> to make " + prefix + " of: <br></font><font face='$HandwrittenFont'><font size='26'>";
                    String potionName = "Recipe: ";//Define recipe name
                    for (int k = 0; k < mgeflist.Count; k++)
                    {
                        if (k > 0)
                        {
                            potionName += " and ";
                            potionString += "<br>";
                        }
                        potionName += mgeflist[k];
                        potionString += mgeflist[k];
                    }
                    String sstring = "";

                    if (mgeflist.Count > 1)
                        sstring = "s";

                    potionString += "<br></font><font size='14'> Contains " + mgeflist.Count + " Effect" + sstring;
                    potionString += "<\\font>";
                    IIngredientGetter[] ingrss = { ingredient, ingr };
                    Program.ourMutex.WaitOne();
                    IngrCombination ingrcombo = new IngrCombination(potionName, ingrss, mgeflist?.ToArray()!, potionString, type);
                    Program.combinations.Add(ingrcombo);
                    Program.ourMutex.ReleaseMutex();
                }
                int j = i + 1 + startIndex;
                foreach (IIngredientGetter ingredient2 in remainingingr)
                {
                    if (ingredient2.Name?.Equals(ingredient.Name) ?? true || String.IsNullOrEmpty(ingredient2.Name?.String) || !ingredient.Effects.Intersect(ingredient2.Effects).Any())
                    {
                        j++;
                        continue;
                    }
                    List<IIngredientGetter> remainingingr2 = Program.allIngredients!.Skip(j).ToList();
                    IIngredientGetter[] potionRecipeList2 = getIngredientsMatchingTwoIngredients(ingredient, ingredient2, remainingingr2);
                    for (int m = 0; m < potionRecipeList2.Length; m++)
                    {
                        IIngredientGetter ingr = potionRecipeList2[m];
                        IEnumerable<IEffectGetter> ActiveEffects = ingr.Effects.Intersect(ingredient.Effects);
                        IEnumerable<IEffectGetter> ActiveEffects2 = ingr.Effects.Intersect(ingredient2.Effects);
                        IEnumerable<IEffectGetter> ActiveEffects3 = ingredient.Effects.Intersect(ingredient2.Effects);
                        ActiveEffects.ToList().AddRange(ActiveEffects2);
                        ActiveEffects.ToList().AddRange(ActiveEffects3);
                        ActiveEffects = ActiveEffects.Distinct();
                        IEffectGetter[] ActiveEffectsA = ActiveEffects.ToArray();
                        if (ActiveEffectsA.Length < 1)
                            continue;
                        String potionString = "<font face='$HandwrittenFont'><font size='26'>";
                        potionString = "-<b>" + (ingredient.Name + "<br></b>-<b>" + ingredient2.Name + "<br></b>-<b>" + ingr.Name + "</b>");
                        List<String?> mgeflist = new List<String?>();
                        List<String?> mgeflists = new List<String?>();
                        List<bool> mgeflistD = new List<bool>();
                        for (int n = 0; n < ActiveEffectsA.Length; n++)
                        {
                            IEffectGetter effect = ActiveEffectsA[n];
                            state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgeffect);
                            mgeflist.Add(mgeffect?.Name?.String);
                            mgeflists.AddRange(mgeffect?.Name?.String?.Split()!);
                            IReadOnlyList<IFormLink<IKeywordGetter>>? thisKeywords = mgeffect.Keywords;
                            if (mgeffect.Flags.HasFlag(MagicEffect.Flag.Detrimental) || mgeffect.Flags.HasFlag(MagicEffect.Flag.Hostile) && (thisKeywords?.Intersect(Program.badKeywordsF!).Any() ?? true))
                                mgeflistD.Add(true);
                            else
                                mgeflistD.Add(false);
                        }
                        String prefix = "Potion";
                        int type = 0;
                        if (mgeflistD.All(x => x.Equals(true)))
                        {
                            prefix = "Poison";
                            type = 1;
                            if (mgeflist.Count <= Program.poisonSkipThreshold)
                                continue;
                            Program.ourMutex.WaitOne();
                            Program.poisonRecipeCount++;
                            Program.ourMutex.ReleaseMutex();
                        }
                        else if (mgeflistD.Contains(true))//Check if effects match poison and potion
                        {
                            prefix = "Impure Potion";
                            type = 2;
                            if (mgeflist.Count <= Program.impureSkipThreshold)
                                continue;
                            Program.ourMutex.WaitOne();
                            Program.impurepotionRecipeCount++;
                            Program.ourMutex.ReleaseMutex();
                        }
                        else
                        {
                            if (mgeflist.Count <= Program.potionSkipThreshold)
                                continue;
                            Program.ourMutex.WaitOne();
                            Program.potionRecipeCount++;
                            Program.ourMutex.ReleaseMutex();
                        }
                        potionString += "</font><font face='$HandwrittenFont'><font size='26'><br> to make " + prefix + " of: <br></font><font face='$HandwrittenFont'><font size='26'>";
                        String potionName = "Recipe: ";
                        for (int k = 0; k < mgeflist.Count; k++)
                        {
                            if (k > 0)
                            {
                                potionName += " and ";
                                potionString += "<br>";
                            }
                            potionName += mgeflist[k];
                            potionString += mgeflist[k];
                        }
                        String sstring = "";

                        if (mgeflist.Count > 1)
                            sstring = "s";
                        potionString += "<br></font><font size='14'> Contains " + mgeflist.Count + " Effect" + sstring;
                        potionString += "<\\font>";
                        IIngredientGetter[] ingrss = { ingredient, ingredient2, ingr };
                        Program.ourMutex.WaitOne();
                        IngrCombination ingrcombo = new IngrCombination(potionName, ingrss, mgeflist?.ToArray()!, potionString, type);
                        Program.combinations.Add(ingrcombo);
                        Program.ourMutex.ReleaseMutex();
                    }
                    j++;
                }
                i++;
                Program.ourMutex.WaitOne();
                Program.totalProcessedCount++;
                if (Program.totalIngredientCount <= Program.totalProcessedCount)
                    Program.finishedProcessing = true;
                Program.ourMutex.ReleaseMutex();
            }
        }
        private IIngredientGetter[] getIngredientsMatchingOneIngredient(IIngredientGetter firstIngredient, IEnumerable<IIngredientGetter> otherIngredients)
        {
            List<IEffectGetter> firstIngredientEffects = firstIngredient.Effects.ToList();
            return (from matchingEffects in otherIngredients.ToList() where (firstIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any()) && (!firstIngredient.IngredientValue.Equals(matchingEffects.IngredientValue)) select matchingEffects).ToArray();

        }
        private IIngredientGetter[] getIngredientsMatchingTwoIngredients(IIngredientGetter firstIngredient, IIngredientGetter secondIngredient, IEnumerable<IIngredientGetter> otherIngredients)
        {
            List<IEffectGetter> firstIngredientEffects = firstIngredient.Effects.ToList();
            List<IEffectGetter> secondIngredientEffects = secondIngredient.Effects.ToList();
            return (from matchingEffects in otherIngredients.ToList() where ((firstIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any()) || (secondIngredientEffects.Intersect(matchingEffects.Effects.ToList()).Any())) && (!firstIngredient.IngredientValue.Equals(matchingEffects.IngredientValue) && !secondIngredient.IngredientValue.Equals(matchingEffects.IngredientValue) && !firstIngredient.IngredientValue.Equals(secondIngredient.IngredientValue)) select matchingEffects).ToArray();
        }
    }
}
static class Extensions
{
    public static IEnumerable<IEnumerable<IIngredientGetter>> Partition<IIngredientGetter>(this IEnumerable<IIngredientGetter> items, int partitionSize)
    {
        return new PartitionHelper<IIngredientGetter>(items, partitionSize);
    }
    private sealed class PartitionHelper<IIngredientGetter> : IEnumerable<IEnumerable<IIngredientGetter>>
    {
        readonly IEnumerable<IIngredientGetter> items;
        readonly int partitionSize;
        bool hasMoreItems;

        internal PartitionHelper(IEnumerable<IIngredientGetter> i, int ps)
        {
            items = i;
            partitionSize = ps;
        }

        public IEnumerator<IEnumerable<IIngredientGetter>> GetEnumerator()
        {
            using (var enumerator = items.GetEnumerator())
            {
                hasMoreItems = enumerator.MoveNext();
                while (hasMoreItems)
                    yield return GetNextBatch(enumerator).ToList();
            }
        }

        IEnumerable<IIngredientGetter> GetNextBatch(IEnumerator<IIngredientGetter> enumerator)
        {
            for (int i = 0; i < partitionSize; ++i)
            {
                yield return enumerator.Current;
                hasMoreItems = enumerator.MoveNext();
                if (!hasMoreItems)
                    yield break;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}