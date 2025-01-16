using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Order;

using TrueUnleveledSkyrim.Config;

namespace TrueUnleveledSkyrim.Patch
{
    class ZonesPatcher
    {
        private static ZoneList? ZonesByKeyword;
        private static ZoneList? ZonesByID;
        private static ZoneKeywordMults? zonesKeywordMults;

        // Returns the level modifiers for the desired NPC based on their race.
        private static void GetLevelMultiplier(EncounterZone encZone, ILinkCache linkCache, out short levelModAdd, out float levelModMult)
        {
            levelModAdd = 0;
            levelModMult = 1f;
            if (!encZone.Location.TryResolve<ILocationGetter>(linkCache, out ILocationGetter? resolvedLocation))
                return;

            Console.WriteLine(resolvedLocation.EditorID);

            for (int i = zonesKeywordMults!.Data.Count - 1; i >= 0; i--)
            {
                ZoneKeywordMultEntry? zoneDefinition = zonesKeywordMults.Data[i];
                foreach (var keywordEntry in resolvedLocation.Keywords.EmptyIfNull())
                {
                    if (!keywordEntry.TryResolve<IKeywordGetter>(linkCache, out IKeywordGetter? resolvedKeyword) || resolvedKeyword.EditorID is null)
                        continue;
                    
                    if (i == 0)
                        Console.WriteLine(resolvedKeyword.EditorID);
                    
                    if (zoneDefinition.Keys.Any(key => resolvedKeyword.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)) && !zoneDefinition.ForbiddenKeys.Any(key => resolvedKeyword.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    {
                        levelModAdd += zoneDefinition.LevelModifierAdd ?? 0;
                        levelModMult += zoneDefinition.LevelModifierMult ?? 0.0f;
                        
                    }
                }
            }
            
        }

        private static void UnlevelZone(EncounterZone encZone, ZoneEntry zoneDefinition, ILinkCache linkCache)
        {
            encZone.Flags.SetFlag(EncounterZone.Flag.MatchPcBelowMinimumLevel, false);
            if(zoneDefinition.EnableCombatBoundary is not null)
                encZone.Flags.SetFlag(EncounterZone.Flag.DisableCombatBoundary, !(bool)zoneDefinition.EnableCombatBoundary);

            if (Patcher.ModSettings.Value.Zones.StaticZoneLevels)
            {
                encZone.MinLevel = (sbyte)Patcher.Randomizer.Next(zoneDefinition.MinLevel, zoneDefinition.MaxLevel);
                encZone.MaxLevel = encZone.MinLevel;
            }
            else
            {
                if (zoneDefinition.MaxLevel == 0)
                {
                    encZone.MinLevel = (sbyte)Patcher.Randomizer.Next(zoneDefinition.MinLevel, zoneDefinition.MinLevel + zoneDefinition.Range);
                    encZone.MaxLevel = 0;
                }
                else
                {
                    encZone.MinLevel = (sbyte)Patcher.Randomizer.Next(zoneDefinition.MinLevel, zoneDefinition.MaxLevel - zoneDefinition.Range + 1);
                    encZone.MaxLevel = (sbyte)(encZone.MinLevel + zoneDefinition.Range);
                }
            }
            GetLevelMultiplier(encZone, linkCache, out short levelModAdd, out float levelModMult);
            int minLevel = (int)((float)(encZone.MinLevel + levelModAdd) * levelModMult);
            if (minLevel < 0)
                minLevel = 0;
            else if (minLevel > 126)
                minLevel = 126;
            int maxLevel = (int)((float)(encZone.MaxLevel + levelModAdd) * levelModMult);
            if (maxLevel < 0)
                maxLevel = 0;
            else if (maxLevel > 126)
                maxLevel = 126;
            encZone.MinLevel = (sbyte)minLevel;
            encZone.MaxLevel = (sbyte)maxLevel;
        }

        private static bool PatchZonesByKeyword(EncounterZone encZone, ILinkCache linkCache)
        {
            if (!encZone.Location.TryResolve<ILocationGetter>(linkCache, out ILocationGetter? resolvedLocation))
                return false;

            for (int i = ZonesByKeyword!.Zones.Count - 1; i >= 0; i--)
            {
                ZoneEntry? zoneDefinition = ZonesByKeyword.Zones[i];
                foreach (var keywordEntry in resolvedLocation.Keywords.EmptyIfNull())
                {
                    if (!keywordEntry.TryResolve<IKeywordGetter>(linkCache, out IKeywordGetter? resolvedKeyword) || resolvedKeyword.EditorID is null)
                        continue;

                    if (zoneDefinition.Keys.Any(key => resolvedKeyword.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)) && !zoneDefinition.ForbiddenKeys.Any(key => resolvedKeyword.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    {
                        UnlevelZone(encZone, zoneDefinition, linkCache);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PatchZonesByID(EncounterZone encZone)
        {
            if (encZone.EditorID is null)
                return false;

            for(int i = ZonesByID!.Zones.Count - 1; i >= 0; i--)
            {
                ZoneEntry? zoneDefinition = ZonesByID.Zones[i];

                if (zoneDefinition.Keys.Any(key => encZone.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)) && !zoneDefinition.ForbiddenKeys.Any(key => encZone.EditorID.Equals(key, StringComparison.OrdinalIgnoreCase)))
                {
                    UnlevelZone(encZone, zoneDefinition, Patcher.LinkCache);
                    return true;
                }
            }

            return false;
        }

        public static void PatchZones(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (Patcher.ModSettings.Value.Zones.UseMorrowlootZoneBalance)
            {
                ZonesByKeyword = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesKeywordMLUPath);
                ZonesByID = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesEDIDMLUPath);
            }
            else
            {
                ZonesByKeyword = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesKeywordPath);
                ZonesByID = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesEDIDPath);
            }

            zonesKeywordMults = JsonHelper.LoadConfig<ZoneKeywordMults>(TUSConstants.ZoneKeywordMultsPath);

            uint processedRecords = 0;
            var forbiddenCache = LoadOrder.Import<ISkyrimModGetter>(state.DataFolderPath, Patcher.ModSettings.Value.Zones.PluginFilter, GameRelease.SkyrimSE).PriorityOrder.ToImmutableLinkCache();
            foreach (var zoneGetter in state.LoadOrder.PriorityOrder.EncounterZone().WinningOverrides())
            {
                // Skip encounter zones that can be found in the cache defined by the plugin filter list.
                if (forbiddenCache.TryResolve(zoneGetter.ToLink(), out _))
                    continue;

                bool wasChanged = false;
                EncounterZone zoneCopy = zoneGetter.DeepCopy();

                wasChanged = PatchZonesByID(zoneCopy) || PatchZonesByKeyword(zoneCopy, Patcher.LinkCache);

                ++processedRecords;
                if (processedRecords % 100 == 0)
                    Console.WriteLine("Processed " + processedRecords + " encounter zones.");

                if (wasChanged)
                {
                    state.PatchMod.EncounterZones.Set(zoneCopy);
                }
            }

            GameSettingFloat? easyEnemyLvlMult = Skyrim.GameSetting.fLeveledActorMultEasy.TryResolve(Patcher.LinkCache)!.DeepCopy() as GameSettingFloat; easyEnemyLvlMult!.Data = new float?(Patcher.ModSettings.Value.Zones.EasySpawnLevelMult); state.PatchMod.GameSettings.Set(easyEnemyLvlMult!);
            GameSettingFloat? mediumEnemyLvlMult = Skyrim.GameSetting.fLeveledActorMultMedium.TryResolve(Patcher.LinkCache)!.DeepCopy() as GameSettingFloat; mediumEnemyLvlMult!.Data = new float?(Patcher.ModSettings.Value.Zones.NormalSpawnLevelMult); state.PatchMod.GameSettings.Set(mediumEnemyLvlMult!);
            GameSettingFloat? hardEnemyLvlMult = Skyrim.GameSetting.fLeveledActorMultHard.TryResolve(Patcher.LinkCache)!.DeepCopy() as GameSettingFloat; hardEnemyLvlMult!.Data = new float?(Patcher.ModSettings.Value.Zones.HardSpawnLevelMult); state.PatchMod.GameSettings.Set(hardEnemyLvlMult!);
            GameSettingFloat? veryHardEnemyLvlMult = Skyrim.GameSetting.fLeveledActorMultVeryHard.TryResolve(Patcher.LinkCache)!.DeepCopy() as GameSettingFloat; veryHardEnemyLvlMult!.Data = new float?(Patcher.ModSettings.Value.Zones.VeryHardSpawnLevelMult); state.PatchMod.GameSettings.Set(veryHardEnemyLvlMult!);

            Console.WriteLine("Processed " + processedRecords + " encounter zones in total.\n");
        }
    }
}
