
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;

namespace MelsEnemyPack
{
    //Same approach as Lordfirespeed
    public class PluginConfig
    {
        //public readonly ConfigEntry<bool> Enabled;
        public ConfigEntry<int> SpawnWeight;
        public ConfigEntry<int> DamageLevel;
        public ConfigEntry<int> ConsumesRessourceValueToReplicate;
        public ConfigEntry<bool> bAutowakeupSuntimer;

        public PluginConfig(BaseUnityPlugin bindingPlugin)
        {
            SpawnWeight = bindingPlugin.Config.Bind("Replicator", "Spawning weight", 100, "The weight for the replicator spawn-wise (rarity)");

            DamageLevel = bindingPlugin.Config.Bind("Replicator", "Damage-tier", 0, "0: default damage (Recommended), 1: reduced damage, 2: Reduced damage and no collision damage");
            ConsumesRessourceValueToReplicate = bindingPlugin.Config.Bind("Replicator", "Scrap-value-replicate", 3, "How much scrap value must be consumed in order to replicate (DO NOT set this to 0)");
            bAutowakeupSuntimer = bindingPlugin.Config.Bind("Replicator", "Awake on daytime end", true, "Should the enemy activate when daytime enemies leaves?");
        }
    }
}