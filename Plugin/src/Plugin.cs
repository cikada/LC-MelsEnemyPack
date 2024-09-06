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
        public static AnomalyType SpringAnomaly;
        internal static new ManualLogSource Logger;

        internal static PluginConfig BoundConfig { get; private set; } = null!;

        //static ConfigFile customFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "mels.enemypack.cfg"), true);


        private void Awake() {
            Logger = base.Logger;
            Assets.PopulateAssets();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            BoundConfig = new PluginConfig(this);

            //SampleEnemy = Assets.MainAssetBundle.LoadAsset<EnemyType>("ExampleEnemy");
            ReplicatorEnemy = Assets.MainAssetBundle.LoadAsset<EnemyType>("ReplicatorEnemy");
            SpringAnomaly = Assets.MainAssetBundle.LoadAsset<AnomalyType>("SpringMine");
            /*var tlTerminalNodeEX = Assets.MainAssetBundle.LoadAsset<TerminalNode>("ExampleEnemyTN");
            var tlTerminalKeywordEX = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("ExampleEnemyTK");*/
            var tlTerminalNodeRT = Assets.MainAssetBundle.LoadAsset<TerminalNode>("ReplicatorEnemyTN");
            var tlTerminalKeywordRT = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("ReplicatorEnemyTK");
            //Logger.LogInfo($"sample {SampleEnemy} !");
            Logger.LogInfo($"repl {ReplicatorEnemy} !");
            //Logger.LogInfo($"sample-pf {SampleEnemy.enemyPrefab} !");
            Logger.LogInfo($"repl-pf {ReplicatorEnemy.enemyPrefab} !");
            Logger.LogInfo($"stat {SpringAnomaly.anomalyName} {SpringAnomaly.anomalyPrefab} !");

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
            // Network Prefabs need to be registered first. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            //NetworkPrefabs.RegisterNetworkPrefab(SampleEnemy.enemyPrefab); 
            NetworkPrefabs.RegisterNetworkPrefab(ReplicatorEnemy.enemyPrefab);

            //RegisterEnemy(SampleEnemy, 100, LevelTypes.All, SpawnType.Outside, tlTerminalNodeEX, tlTerminalKeywordEX);
            RegisterEnemy(ReplicatorEnemy, BoundConfig.SpawnWeight.Value, LevelTypes.All, SpawnType.Default, tlTerminalNodeRT, tlTerminalKeywordRT);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            Assets.MainAssetBundle.Unload(false);
        }
    }

    public static class Assets {
        public static AssetBundle MainAssetBundle = null;
        public static void PopulateAssets() {
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if(MainAssetBundle == null)
                MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "mels.lethalbundle"));
            if (MainAssetBundle == null) {
                Plugin.Logger.LogError("Failed to load custom assets.");
                return;
            }
        }
    }

    [HarmonyPatch(typeof(RoundManager))]
    internal class MapObjectRoundManagerPatch
    {
        public static bool AddCustomStaticToMap(RoundManager __instance, string[] RequiredNames, AnomalyType ToSpawn, bool bSpawnFacingAwayFromWall = true, int AmountToSpawn = 1)
        {
            //Note: This uses an AnomalyType since it have the requried parameters, and we cannot LoadAsset an actual SpawnableMapObject
            //Note: It uses the probabilityCurve for amount to include. If empty, will use the AmountToSpawn variable
            if (ToSpawn == null || ToSpawn.anomalyPrefab == null)
                return false;

            //Add to map RandomMapObjects (required)
            bool bFound = AddToRandomMapObjects(RequiredNames, ToSpawn.anomalyPrefab);

            if (bFound)
            {
                //If we managed to add it to atleast one valid spawner
                //Make the actual entry we are pushing
                SpawnableMapObject NewEntry = new SpawnableMapObject();
                NewEntry.prefabToSpawn = ToSpawn.anomalyPrefab;
                NewEntry.spawnFacingAwayFromWall = bSpawnFacingAwayFromWall;

                AnimationCurve SpawnAmount;
                if(ToSpawn.probabilityCurve.length == 0)
                {
                    //Not enough points supplied
                    SpawnAmount = new AnimationCurve();
                    Keyframe kf = new Keyframe();
                    kf.value = AmountToSpawn;
                    kf.time = 0;
                    SpawnAmount.AddKey(kf);
                }
                else
                {
                    SpawnAmount = ToSpawn.probabilityCurve;
                }
                NewEntry.numberToSpawn = SpawnAmount;

                //Expand the existing array by one, to fit this
                SpawnableMapObject[] tempArr = new SpawnableMapObject[__instance.currentLevel.spawnableMapObjects.Length + 1];
                for (int i = 0; i < __instance.currentLevel.spawnableMapObjects.Length; i++)
                {
                    tempArr[i] = __instance.currentLevel.spawnableMapObjects[i]; //Copy existing
                }
                //Add our new entry
                tempArr[tempArr.Length - 1] = NewEntry;
                //Update the original array
                __instance.currentLevel.spawnableMapObjects = tempArr;

                return true;
            }

            return false;
        }

        public static bool AddToRandomMapObjects(string[] RequiredNames, GameObject Prefab)
        {
            //Add to the RandomMapObjects that exist on the given level. This is required for it to spawn
            bool bFound = false;
            RandomMapObject[] array = UnityEngine.Object.FindObjectsOfType<RandomMapObject>();
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                    continue;

                for (int j = 0; j < array[i].spawnablePrefabs.Count; j++)
                {
                    bool bValidName = false;
                    //Look through the found RandomMapObjects - Look for any specified names it must match (Such as landmine)
                    if (RequiredNames.Length == 0)
                        bValidName = true;
                    else
                    {
                        for (int k = 0; k < RequiredNames.Length; k++)
                        {
                            if (array[i].spawnablePrefabs[j].name.ToLower() == RequiredNames[k].ToLower())
                            {
                                bValidName = true;
                            }
                        }
                    }
                    //Valid, add it
                    if(bValidName)
                    {
                        bFound = true; //We have atleast one valid spawn
                        array[i].spawnablePrefabs.Add(Prefab);
                    }
                }
            }

            return bFound;
        }


        //Hook onto RoundManager's SpawnMapObjects() and run this just before
        [HarmonyPatch(nameof(RoundManager.SpawnMapObjects))]
        [HarmonyPrefix]
        static private void SpawnMapObjectsPrefix(RoundManager __instance)
        {
            //What types needs to be allowed to spawn on the level already, for us to include our custom type
            //Not case sensitive. Leave blank for any.
            //Plugin.SpringAnomaly is a AnomalyType loaded through LoadAsset from the main plugin
            /*bool bSuccess = AddCustomStaticToMap(__instance, new string[] { "Landmine" }, Plugin.SpringAnomaly);
            if(bSuccess)
            {
                Plugin.Logger.LogInfo($"Added static {Plugin.SpringAnomaly.anomalyName} - {Plugin.SpringAnomaly.anomalyPrefab.name} !");
            }*/
        }
    }
}
