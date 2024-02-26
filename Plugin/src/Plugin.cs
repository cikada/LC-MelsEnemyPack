using System.Reflection;
using UnityEngine;
using BepInEx;
using HarmonyLib;
using LethalLib.Modules;
using static LethalLib.Modules.Levels;
using static LethalLib.Modules.Enemies;
using BepInEx.Logging;
using System.IO;
using BepInEx.Configuration;

namespace MelsEnemyPack
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)] 
    public class Plugin : BaseUnityPlugin {
        public static Harmony _harmony;
        public static EnemyType SampleEnemy;
        public static EnemyType ReplicatorEnemy;
        internal static new ManualLogSource Logger;

        internal static PluginConfig BoundConfig { get; private set; } = null!;

        //static ConfigFile customFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "mels.enemypack.cfg"), true);


        private void Awake() {
            Logger = base.Logger;
            Assets.PopulateAssets();

            BoundConfig = new PluginConfig(this);

            //SampleEnemy = Assets.MainAssetBundle.LoadAsset<EnemyType>("ExampleEnemy");
            ReplicatorEnemy = Assets.MainAssetBundle.LoadAsset<EnemyType>("ReplicatorEnemy");
            /*var tlTerminalNodeEX = Assets.MainAssetBundle.LoadAsset<TerminalNode>("ExampleEnemyTN");
            var tlTerminalKeywordEX = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("ExampleEnemyTK");*/
            var tlTerminalNodeRT = Assets.MainAssetBundle.LoadAsset<TerminalNode>("ReplicatorEnemyTN");
            var tlTerminalKeywordRT = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("ReplicatorEnemyTK");
            //Logger.LogInfo($"sample {SampleEnemy} !");
            Logger.LogInfo($"repl {ReplicatorEnemy} !");
            //Logger.LogInfo($"sample-pf {SampleEnemy.enemyPrefab} !");
            Logger.LogInfo($"repl-pf {ReplicatorEnemy.enemyPrefab} !");

            // Network Prefabs need to be registered first. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            //NetworkPrefabs.RegisterNetworkPrefab(SampleEnemy.enemyPrefab); 
            NetworkPrefabs.RegisterNetworkPrefab(ReplicatorEnemy.enemyPrefab);

            //RegisterEnemy(SampleEnemy, 100, LevelTypes.All, SpawnType.Outside, tlTerminalNodeEX, tlTerminalKeywordEX);
            RegisterEnemy(ReplicatorEnemy, BoundConfig.SpawnWeight.Value, LevelTypes.All, SpawnType.Default, tlTerminalNodeRT, tlTerminalKeywordRT);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Required by https://github.com/EvaisaDev/UnityNetcodePatcher maybe?
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }

    public static class Assets {
        public static AssetBundle MainAssetBundle = null;
        public static void PopulateAssets() {
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "mels.lethalbundle"));
            if (MainAssetBundle == null) {
                Plugin.Logger.LogError("Failed to load custom assets.");
                return;
            }
        }
    }
}