using System.Collections;
using System.Collections.Generic;
using BepInEx;
using System.IO;
using BepInEx.Configuration;
using GameNetcodeStuff;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements.Experimental;
using UnityEngine.UIElements;
using HarmonyLib;

namespace MelsEnemyPack
{

    // You may be wondering, how does the Example Enemy know it is from class MelsEnemyPackAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class ReplicatorEnemyAI : EnemyAI {

        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
        #pragma warning disable 0649
        public Transform turnCompass;
        public Transform attackArea;
        public AISearchRoutine scoutingSearchRoutine;
        #pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        System.Random enemyRandom;
        bool isDeadAnimationDone;
        
        /*
            configs
         */
        bool bStartDocile = true; //Start us out as docile?
        int MaxSpawns = 3; //n! maxspawns
                           
        //static ConfigFile customFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "mels.enemypack.cfg"), true);
        
        //bool bDoorsCountAsScrap = true; //Eating doors will also give us value
        bool bAutowakeupSuntimer = true; //Should we wake up when daytime enemies leave?
        int DamageLevel = 0; //Should this enemy be easier?
        int ConsumesRessourceValueToReplicate = 3; //This value or higher scrap value eaten means we spawn a child

        /*
         Enemy variables
         */
        bool bPersonallyAggroed; //Was I personally triggered?
        bool bGroupAggroed; //Was I triggered by my friends
        GrabbableObject ObjectOfInterest; //What am I currently interested in interacting with?
        struct ClaimedObjects
        {
            public GrabbableObject claimedObject;
            public float time;
        };
        List<ClaimedObjects> grabbableClaimed = new List<ClaimedObjects>();
        GrabbableObject lastClaim;
        //targetPlayer
        int PersonallySpawned = 0; //How many have I been responsible for spawning
        ReplicatorEnemyAI Parent; //Who spawned me?
        int CurrentlyConsumedScrap = 0; //How much have we eaten?
        int MyMaxSpawns = 0; //How many am I allowed to spawn?
        bool bPlayedSpawnAnimation = false; //Did I play my spawn anim?
        bool bLurking = false;
        float EnteredStateTime = 0f;
        public float FriendlyAggroRange = 30f;
        bool bCaresAboutPlayer = false;
        public GameObject PrefabToSpawn;

        public AnimationCurve fallToGroundCurve;
        private Vector3[] legPositions = new Vector3[4];
        private Vector3[] oldLegPositions = new Vector3[4];
        private Vector3[] newLegPositions = new Vector3[4];

        private Vector3[] oldLegNormal = new Vector3[4];
        private Vector3[] currentLegNormal = new Vector3[4];
        private Vector3[] newLegNormal = new Vector3[4];
        [SerializeField] LayerMask terrainLayer = default;
        [SerializeField] Vector3[] footOffset = default;

        private float[] legLerp = new float[4];

        public Transform body = default;
        public float stepHeight = 0.3f;

        public Transform[] legDefaultPositions;
        public Transform[] legTargets;

        public Transform debugSphere1;
        public Transform debugSphere2;

        public AudioSource footstepAudio;
        public AudioClip[] footstepSFX;

        public AudioClip AttackSFX;
        public AudioClip ConsumeSFX;
        public AudioClip CallForHelpSFX;
        public AudioClip StartupSFX;
        public AudioClip hitGroundSFX;
        public AudioClip DeathSound;
        private float LastConsumeSFX;
        public Vector3 DesiredDestination;

        public ParticleSystem Poof;
        public ParticleSystem Grind;
        public ParticleSystem Death;

        private float updateOffsetPositionInterval;
        private Vector3 offsetTargetPos;
        private bool clingingToCeiling;
        public Transform modelContainer;
        private Ray ray;
        private Coroutine ceilingAnimationCoroutine;
        public Vector3 ceilingHidingPoint;
        private int offsetNodeAmount = 6;
        private Vector3 propelVelocity = Vector3.zero;
        private RaycastHit rayHit;
        private bool startedCeilingAnimationCoroutine;
        private Vector3 wallPosition;
        private Vector3 wallNormal;
        bool bDesiresCeiling, bDesiresWall;
        private Vector3 defaultGravity;
        private int EnemyMask = 524288;

        private Vector3[] legSpacing = new Vector3[4];
        private float[] legDistances = new float[4]
        {
            0.5f, //FR
            0.5f, //FL
            0.35f, //BR
            0.35f  //BL
        };

        float timeSinceActuallySpawned;
        float PossiblyStuckTime;
        float timeSinceLastCheck;
        ObstacleAvoidanceType defaultObstacleAvoidanceType;
            
        public static List<GameObject> grabbableObjectsInMap = new List<GameObject>();
        /*
         Does SetDestination need to be inside a 
        ChooseClosestNodeToPosition(targetPlayer.transform.position, avoidLineOfSight: false, 4).transform.position
        ?
         */

        public enum State {
            SearchingForPlayer,
            StickingInFrontOfPlayer,
            HeadSwingAttackInProgress,
            //New
            ReplicatorStateDocile,
            ReplicatorStateActive,
            ReplicatorStateNomming,
            ReplicatorStateRoaming,
            ReplicatorStateFleeing,
            ReplicatorStateDefending,
            ReplicatorStateSpawnAnim,
            ReplicatorStateFindDocileLocation,
            ReplicatorStateReplicating
        }

        public void Awake()
        {
            DamageLevel = Plugin.BoundConfig.DamageLevel.Value;
            ConsumesRessourceValueToReplicate = Plugin.BoundConfig.ConsumesRessourceValueToReplicate.Value;
            bAutowakeupSuntimer = Plugin.BoundConfig.bAutowakeupSuntimer.Value;
            Debug.Log(".." + DamageLevel+".." + ConsumesRessourceValueToReplicate + ".." + bAutowakeupSuntimer);
        }

        public void LateUpdate()
        {
            MoveLegsProcedurally();
            //MoveLegsProcedurallyNew();
        }

        public int GetDamageForAction(bool bDirectHit = false, bool bCollision = false)
        {
            if (DamageLevel == 0)
            {
                //defaults
                if (bDirectHit)
                {
                    return 40;
                }
                else if (bCollision)
                {
                    return 20;
                }
            }
            else if (DamageLevel == 1)
            {
                //defaults
                if (bDirectHit)
                {
                    return 20;
                }
                else if (bCollision)
                {
                    return 10;
                }
            }
            else if (DamageLevel == 2)
            {
                //defaults
                if (bDirectHit)
                {
                    return 20;
                }
                else if (bCollision)
                {
                    return 0;
                }
            }

            return 20;
        }

        [ClientRpc]
        public void SyncConfigClientRPC(int repRessourcesDemand, int repDamageLevel, bool repSunset)
        {
            ConsumesRessourceValueToReplicate = repRessourcesDemand;
            DamageLevel = repDamageLevel;
            bAutowakeupSuntimer = repSunset;
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Replicator Enemy Spawned");
            timeSinceHittingLocalPlayer = 0;
            creatureAnimator.SetTrigger("startWalk");
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;

            //Sync config options
            if(IsServer)
                SyncConfigClientRPC(ConsumesRessourceValueToReplicate, DamageLevel, bAutowakeupSuntimer);

            for (int i = 0; i < legDefaultPositions.Length; i++)
            {
                if(legDefaultPositions[i] == null)
                {
                    Debug.Log("wtf");
                }
                legPositions[i] = legDefaultPositions[i].position;
                newLegPositions[i] = legDefaultPositions[i].position;
                oldLegPositions[i] = legDefaultPositions[i].position;
                legSpacing[i] = legDefaultPositions[i].position;
                currentLegNormal[i] = transform.up;
                newLegNormal[i] = transform.up;
                oldLegNormal[i] = transform.up;
                legLerp[i] = 1;
            }
            openDoorSpeedMultiplier = 0.6f;

            if (body == default)
            {
                body = gameObject.transform;
            }
            defaultGravity = Physics.gravity;
            defaultObstacleAvoidanceType = agent.obstacleAvoidanceType;
            //footSpacing = transform.localPosition.x;

            //Reset my variables
            if (Parent == null)
            {
                MyMaxSpawns = MaxSpawns;
                PersonallySpawned = 0;
                Parent = null;
                CurrentlyConsumedScrap = 0;
                ObjectOfInterest = null;
                bGroupAggroed = false;
                bPersonallyAggroed = false;
            }

            if (Poof != null)
            {
                Poof.Stop();
            }
            if (Grind != null)
            {
                Grind.Stop();
            }
            if (Death != null)
            {
                Death.Stop();
            }

            if (bStartDocile && TimeOfDay.Instance.currentLevelWeather != LevelWeatherType.Eclipsed) // Check if eclipsed
            {
                SetCurrentBehaviourState(State.ReplicatorStateFindDocileLocation);
            }
            else
            {
                SetCurrentBehaviourState(State.ReplicatorStateSpawnAnim);
            }

            RefreshGrabbableObjectsInMapList();
        }

        public void TryReplicate()
        {
            if (!IsServer)
                return;
            if (PersonallySpawned >= MyMaxSpawns)
                return;

            if (CurrentlyConsumedScrap < ConsumesRessourceValueToReplicate)
                return;
            
            CurrentlyConsumedScrap -= ConsumesRessourceValueToReplicate;
            PersonallySpawned++;
            LogIfDebugBuild("Spawning new replicator enemy");
            //Spawn a child
            /*if (PrefabToSpawn != null)
            {
                GameObject child = Instantiate(PrefabToSpawn, base.transform.position, Quaternion.identity);
                ReplicatorEnemyAI childScript = null;
                if (child != null)
                    childScript = child.GetComponent<ReplicatorEnemyAI>();
                if(childScript != null)
                    childScript.SpawnedByParent(MyMaxSpawns - 1, this);
            }*/
            NetworkObjectReference q = RoundManager.Instance.SpawnEnemyGameObject(base.transform.position, base.transform.rotation.y, -1, enemyType);
            if (q.TryGet(out var networkObject))
            {
                ReplicatorEnemyAI childScript = networkObject.gameObject.GetComponent<ReplicatorEnemyAI>();

                if (childScript != null)
                    childScript.SpawnedByParent(MyMaxSpawns - 1, this);
            }
            ulong target = 0;
            if(targetPlayer != null)
            {
                target = targetPlayer.playerClientId;
            }
            ReplicateValuesClientRpc(PersonallySpawned, CurrentlyConsumedScrap, bGroupAggroed, bPersonallyAggroed, MyMaxSpawns, /*GetObjectOfInterest(),*/ target);

            //workaround for replicating obj of interest (Since can't be null)
            if(GetObjectOfInterest() != null)
                ReplicateObjectOfInterestClientRpc(GetObjectOfInterest());
        }

        [ClientRpc]
        public void DesiresCeilingWallClientRPC(bool bNewDesiresCeiling, bool bNewDesiresWall, Vector3 NewCeilingPoint, Vector3 NewDesiredPosition)
        {
            bDesiresCeiling = bNewDesiresCeiling;
            bDesiresWall = bNewDesiresWall;

            if(bDesiresCeiling || bDesiresWall)
            {
                startedCeilingAnimationCoroutine = true;
                if(bDesiresCeiling)
                {
                    ceilingAnimationCoroutine = StartCoroutine(clingToCeiling());
                }
            }
            else
            {
                startedCeilingAnimationCoroutine = false;
            }
        }

        public NetworkObject GetObjectOfInterest()
        {
            if (ObjectOfInterest == null)
                return null;

            return ObjectOfInterest.GetComponent<NetworkObject>();
        }
        public NetworkObject GetReplicatorEnemyAI(ReplicatorEnemyAI a)
        {
            if (a == null)
                return null;
            return a.GetComponent<NetworkObject>();
        }

        [ClientRpc]
        public void ReplicateObjectOfInterestClientRpc(NetworkObjectReference RepObjectOfInterest)
        {
            ObjectOfInterest = null;
            if (RepObjectOfInterest.TryGet(out var networkObject))
            {
                if (networkObject == null || networkObject.gameObject == null || networkObject.GetComponent<GrabbableObject>() == null)
                    ObjectOfInterest = null;
                else
                    ObjectOfInterest = networkObject.gameObject.GetComponent<GrabbableObject>();
            }
        }

       [ClientRpc]
        public void ReplicateValuesClientRpc(int RepPersonallySpawned, int RepConsumedScrap, bool RepGroupAggro, bool RepSelfAggro, int RepMaxSpawns/*, NetworkObjectReference RepObjectOfInterest*/, ulong RepPlayerOfInterest)
        {
            PersonallySpawned = RepPersonallySpawned;
            CurrentlyConsumedScrap = RepConsumedScrap;
            bGroupAggroed = RepGroupAggro;
            bPersonallyAggroed = RepSelfAggro;
            MyMaxSpawns = RepMaxSpawns;
            /*if (RepObjectOfInterest.TryGet(out var networkObject))
            {
                if (networkObject == null || networkObject.gameObject == null || networkObject.GetComponent<GrabbableObject>() == null)
                    ObjectOfInterest = null;
                else
                    ObjectOfInterest = networkObject.gameObject.GetComponent<GrabbableObject>();
            }*/
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                if (StartOfRound.Instance.allPlayerScripts[i].actualClientId == RepPlayerOfInterest)
                {
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                    break;
                }
            }
        }

        public void SpawnedByParent(int MaxSpawns, ReplicatorEnemyAI Source)
        {
            if (!IsServer) 
                return;

            PersonallySpawned = 0;
            Parent = Source;
            CurrentlyConsumedScrap = 0;
            ObjectOfInterest = null;
            bGroupAggroed = false;
            bPersonallyAggroed = false;
            MyMaxSpawns = MaxSpawns;

            if (Source != null)
            {
                //Inherit hostility
                bGroupAggroed = Source.bGroupAggroed;
                if(!bGroupAggroed)
                {
                    bGroupAggroed = Source.bPersonallyAggroed;
                }
            }
            ventAnimationFinished = true;
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("inSpawningAnimation", value: false);
            }

            SpawnedByParentClientRPC(MyMaxSpawns, bGroupAggroed, GetReplicatorEnemyAI(Source));
        }

        [ClientRpc]
        public void SpawnedByParentClientRPC(int RepMaxSpawns, bool bRepGroupAggroed, NetworkObjectReference Source)
        {
            Parent = null;
            if (Source.TryGet(out var networkObject))
            {
                if (networkObject == null || networkObject.gameObject == null || networkObject.GetComponent<ReplicatorEnemyAI>() == null)
                    Parent = null;
                else
                    Parent = networkObject.gameObject.GetComponent<ReplicatorEnemyAI>();
            }
            ventAnimationFinished = true;
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("inSpawningAnimation", value: false);
            }

            PersonallySpawned = 0;
            CurrentlyConsumedScrap = 0;
            ObjectOfInterest = null;
            bGroupAggroed = bRepGroupAggroed;
            bPersonallyAggroed = false;
            MyMaxSpawns = RepMaxSpawns;
        }


        void LogIfDebugBuild(string text) {
            #if DEBUG
            Plugin.Logger.LogInfo(text+" (From "+ thisEnemyIndex +")");
            #endif
        }
        public static void RefreshGrabbableObjectsInMapList()
        {
            grabbableObjectsInMap.Clear();
            GrabbableObject[] array = Object.FindObjectsOfType<GrabbableObject>();
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] != null && array[i].grabbable)
                {
                    grabbableObjectsInMap.Add(array[i].gameObject);
                }
            }
        }
        
        public void ClaimObject(GrabbableObject grab)
        {
            if (grab == lastClaim)
                return;
            //Tell any replicator nearby
            Collider[] results = Physics.OverlapSphere(base.transform.position, 80, EnemyMask);
            ReplicatorEnemyAI tc = null;
            //Debug.Log(results.Length);
            for (int i = 0; i < results.Length; i++)
            {
                //Debug.Log(results[i].gameObject);
                if (results[i] == null)
                    continue;
                tc = results[i].gameObject.GetComponent<ReplicatorEnemyAI>();
                if (tc == null)
                    tc = results[i].gameObject.GetComponentInParent<ReplicatorEnemyAI>();
                if (tc == null)
                    continue;
                if (tc == this)
                    continue;
                tc.ClaimObjectClientRPC(grab.NetworkObject);
            }
        }

        [ClientRpc]
        public void ClaimObjectClientRPC(NetworkObjectReference claim)
        {
            if (claim.TryGet(out var networkObject))
            {
                if (networkObject == null || networkObject.gameObject == null || networkObject.GetComponent<GrabbableObject>() == null)
                    return;

                GrabbableObject k = networkObject.gameObject.GetComponent<GrabbableObject>();

                if(k == null) return;
                ClaimedObjects newObj = new ClaimedObjects();
                for (int i = 0; i < grabbableClaimed.Count; i++)
                {
                    if (grabbableClaimed[i].claimedObject == k)
                    {
                        newObj = grabbableClaimed[i];
                        newObj.time = Time.time;
                        grabbableClaimed[i] = newObj;
                        if (ObjectOfInterest == k)
                            ObjectOfInterest = null;
                        return;
                    }
                }
                newObj.time = Time.time;
                newObj.claimedObject = k;
                grabbableClaimed.AddItem(newObj);
                if (k == ObjectOfInterest)
                    ObjectOfInterest = null;
            }
        }

        public bool CheckIfObjectClaimed(GrabbableObject grab)
        {
            for(int i = 0;i< grabbableClaimed.Count;i++)
            {
                if (Time.time - grabbableClaimed[i].time < 20f)
                {
                    if (grab == grabbableClaimed[i].claimedObject)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void SetCurrentBehaviourState(State NewState, Vector3 NewPOI = new Vector3())
        {
            if (!IsServer)
                return;

            if(currentBehaviourStateIndex != (int)NewState)
            {
                DesiredDestination = NewPOI;
                LogIfDebugBuild("Replicator Enemy entering new state " + NewState+ " Old state: "+currentBehaviourStateIndex);
                currentBehaviourStateIndex = (int)NewState;
                EnteredStateTime = timeSinceActuallySpawned;
                SetBehaviourStateClientRpc(currentBehaviourStateIndex, NewPOI);
                bDesiresCeiling = false;
                bDesiresWall = false;
            }
            else
            {
                LogIfDebugBuild("Replicator Enemy tried entering " + NewState+" but was already doing it");
            }
        }

        [ClientRpc]
        public void SetBehaviourStateClientRpc(int NewState, Vector3 NewPOI = new Vector3())
        {
            currentBehaviourStateIndex = NewState;
            DesiredDestination = NewPOI;
            LogIfDebugBuild("(client) Replicator Enemy entering new state " + NewState + " Old state: " + currentBehaviourStateIndex);
            SwitchToBehaviourClientRpc(NewState);
        }

        public override void Update()
        {
            base.Update();
            if(isEnemyDead){
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if(!isDeadAnimationDone){ 
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;
            if(false && targetPlayer != null && PlayerIsTargetable(targetPlayer) && !scoutingSearchRoutine.inProgress){
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }


            if (bDesiresCeiling)
            {
                gameObject.transform.position = Vector3.SmoothDamp(gameObject.transform.position, ceilingHidingPoint, ref propelVelocity, 0.1f); 
                base.transform.eulerAngles = new Vector3(0f, 0f, 180);
            }
            else if (bDesiresWall)
            {
                gameObject.transform.position = Vector3.SmoothDamp(gameObject.transform.position, wallPosition, ref propelVelocity, 0.1f);
            }
            //Bit of a hack, but inSpecialAnimation and agent.enabled is required for our thing to not glitch when clinging to ceilings or walls - 
            //However, this also disables the AIInterval ticks..
            //So to work around this, we have our own "variant" here
            if(bDesiresCeiling || bDesiresWall)
            {
                if (timeSinceActuallySpawned - timeSinceLastCheck > AIIntervalTime)
                {
                    timeSinceLastCheck = timeSinceActuallySpawned;
                    targetPlayer = GetClosestPlayer();
                    if (NearbyTargetWithLoot(true, true) || (bAutowakeupSuntimer && (!(TimeOfDay.Instance == null) && TimeOfDay.Instance.normalizedTimeOfDay > enemyType.normalizedTimeInDayToLeave)))
                    {
                        //If we spot someone wearing loot, go back to states
                        inSpecialAnimation = false;
                        agent.enabled = true;
                        bDesiresCeiling = false;
                        bDesiresWall = false;

                        DesiresCeilingWallClientRPC(bDesiresCeiling, bDesiresWall, ceilingHidingPoint, DesiredDestination);

                        SetCurrentBehaviourState(State.ReplicatorStateActive);
                    }
                }
            }

            timeSinceActuallySpawned += Time.deltaTime;
        }

        IEnumerator DoDeath()
        {
            if (Death != null)
            {
                Death.Play();
            }
            if (Grind != null)
            {
                Grind.Stop();
            }
            if (DeathSound != null && creatureSFX != null)
            {
                creatureSFX.pitch = Random.Range(0.6f, 1.2f);
                creatureSFX.PlayOneShot(DeathSound, Random.Range(0.7f, 1f));
                WalkieTalkie.TransmitOneShotAudio(creatureSFX, DeathSound, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            }
            DoDeathClientRpc();

            float deathTime = 0f;
            while (deathTime < 1f)
            {
                yield return null;
                deathTime += Time.deltaTime;
            }

            Destroy(gameObject);
        }

        [ClientRpc]
        public void DoDeathClientRpc()
        {
            StartCoroutine(DoDeathClient());
        }
        IEnumerator DoDeathClient()
        {
            if (Death != null)
            {
                Death.Play();
            }
            if(Grind != null)
            {
                Grind.Stop();
            }
            if (DeathSound != null && creatureSFX != null)
            {
                creatureSFX.pitch = Random.Range(0.6f, 1.2f);
                creatureSFX.PlayOneShot(DeathSound, Random.Range(0.1f, 1f));
                WalkieTalkie.TransmitOneShotAudio(creatureSFX, DeathSound, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            }

            float deathTime = 0f;
            while (deathTime < 1f)
            {
                yield return null;
                deathTime += Time.deltaTime;
            }

            Destroy(gameObject);
        }

        /*
         
        public AudioClip AttackSFX;
        public AudioClip ConsumeSFX;
        public AudioClip CallForHelpSFX;
        public AudioClip StartupSFX;
         */

        private void StopCreatureSFX(bool bSound = true)
        {
            if (bSound)
                creatureSFX.Stop();
            if (Grind != null)
            {
                Grind.Stop();
            }
            if (IsServer)
                StopCreatureSFXClientRPC();
        }
        [ClientRpc]
        private void StopCreatureSFXClientRPC(bool bSound = true)
        {
            if (bSound)
                creatureSFX.Stop();
            if (Grind != null)
            {
                Grind.Stop();
            }
        }

        private void PlayWakeSFX()
        {
            if (StartupSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(StartupSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, StartupSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            if (IsServer)
                PlayWakeSFXClientRPC();
        }
        [ClientRpc]
        private void PlayWakeSFXClientRPC()
        {
            if (StartupSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(StartupSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, StartupSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
        }
        private void PlayHitGroundSFX()
        {
            if (hitGroundSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(hitGroundSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, hitGroundSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            if (IsServer)
                PlayHitGroundSFXClientRPC();
        }
        [ClientRpc]
        private void PlayHitGroundSFXClientRPC()
        {
            if (hitGroundSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(hitGroundSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, hitGroundSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
        }
        private void PlayAlertSFX()
        {
            if (CallForHelpSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(CallForHelpSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, CallForHelpSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            if (IsServer)
                PlayAlertSFXClientRPC();
        }
        [ClientRpc]
        private void PlayAlertSFXClientRPC()
        {
            if (CallForHelpSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(CallForHelpSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, CallForHelpSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
        }
        private void PlayEatingFX()
        {
            if (ConsumeSFX == null || creatureSFX == null)
                return;
            if (timeSinceActuallySpawned - LastConsumeSFX < ConsumeSFX.length)
                return;

            creatureSFX.Stop();
            //eating fx
            if (Grind != null)
            {
                Grind.Play();
            }
            LastConsumeSFX = timeSinceActuallySpawned;
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(ConsumeSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, ConsumeSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            if (IsServer)
                PlayEatingFXClientRPC();
        }
        [ClientRpc]
        private void PlayEatingFXClientRPC()
        {
            if (ConsumeSFX == null || creatureSFX == null)
                return;
            if (timeSinceActuallySpawned - LastConsumeSFX < ConsumeSFX.length)
                return;

            creatureSFX.Stop();
            //eating fx
            if (Grind != null)
            {
                Grind.Play();
            }
            LastConsumeSFX = timeSinceActuallySpawned;
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(ConsumeSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, ConsumeSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
        }
        private void PlayAttackFX()
        {
            if (AttackSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(AttackSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, AttackSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            if (IsServer)
                PlayAttackFXClientRPC();
        }
        [ClientRpc]
        private void PlayAttackFXClientRPC()
        {
            if (AttackSFX == null || creatureSFX == null)
                return;
            creatureSFX.Stop();
            creatureSFX.pitch = Random.Range(0.6f, 1.2f);
            creatureSFX.PlayOneShot(AttackSFX, Random.Range(0.6f, 1f));
            WalkieTalkie.TransmitOneShotAudio(creatureSFX, AttackSFX, Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
        }

        public bool IsHostile()
        {
            return bGroupAggroed || bPersonallyAggroed;
        }

        public float MatchTargetSpeed(bool bWantsFaster = false)
        {
            float bonusspeed = 0;
            if (bWantsFaster) //We want to go significently faster
                bonusspeed = 5f;
            if (targetPlayer != null)
            {
                //TODO: Make sure this is "slightly faster" than the players movement in these states
                if (targetPlayer.isSprinting)
                    return 20f + bonusspeed;
                else if (targetPlayer.isCrouching)
                    return 5f + bonusspeed;
                else
                    return 10f + bonusspeed;
            }
            return 10f + bonusspeed;
        }

        public bool NearbyTargetWithLoot(bool bCheckLOS = false, bool bAnyLoot = false)
        {
            if (targetPlayer != null)
            {
                if (targetPlayer.currentlyHeldObject != null || targetPlayer.currentlyHeldObjectServer != null)
                {
                    if (!bCheckLOS)
                        return true;
                    if (CanSeePoint(targetPlayer.gameplayCamera.transform.position, 80f))
                        return true;
                }
                else if(bAnyLoot)
                {
                    for(int i=0; i< targetPlayer.ItemSlots.Length; i++)
                    {
                        if (targetPlayer.ItemSlots[i] != null && targetPlayer.ItemSlots[i].grabbable && targetPlayer.ItemSlots[i].itemProperties.isConductiveMetal)
                        {
                            if (!bCheckLOS)
                                return true;
                            if (CanSeePoint(targetPlayer.gameplayCamera.transform.position, 80f))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool CanSeePoint(Vector3 Point, float Range)
        {
            Vector3 MyPoint = base.transform.position;
            if(eye != null)
            {
                MyPoint = eye.position;
            }
            if (Vector3.Distance(MyPoint, Point) < (float)Range && !Physics.Linecast(MyPoint, Point, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                return true;
            }
            return false;
        }

        public GrabbableObject TryFindNearbyScrap(int range = 20, bool bOmniPotent = false)
        {
            //Find nearby scrap
            float bestDistance = range;
            GrabbableObject BestObject = null;
            if (bOmniPotent)
            {
                for(int i=0;i<grabbableObjectsInMap.Count;i++)
                {
                    if (grabbableObjectsInMap[i] != null)
                    {
                        GrabbableObject component = grabbableObjectsInMap[i].GetComponent<GrabbableObject>();
                        if ((bool)component && (!component.isHeld))
                        {
                            if ((grabbableObjectsInMap[i].transform.position - base.transform.position).magnitude < bestDistance)
                            {
                                BestObject = component;
                                bestDistance = range;
                            }
                        }
                    }
                }
                if(BestObject != null)
                {
                    if(CheckIfObjectClaimed(BestObject))
                    {
                        BestObject = null;
                    }
                }
                if (BestObject != null)
                {
                    //Since objects can be spawned at unreachable positions, we gotta explicitly check if we can reach it
                    Vector3 position = BestObject.transform.position;
                    position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
                    path1 = new NavMeshPath();
                    if (!agent.CalculatePath(position, path1))
                    {
                        BestObject = null;
                    }

                    if (BestObject != null && Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
                    {
                        BestObject = null;
                    }
                }
                return BestObject;
            }
            else
            {
                GameObject gameObject2 = CheckLineOfSight(grabbableObjectsInMap, 60f, range, 5f);
                if ((bool)gameObject2)
                {
                    GrabbableObject component = gameObject2.GetComponent<GrabbableObject>();
                    if ((bool)component && (!component.isHeld))
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            }
            // Sets scoutingSearchRoutine.inProgress to True if serching, False if found player
            // Will set targetPlayer to the closest player
            if(bCaresAboutPlayer)
                TargetClosestPlayer();
            if ((mostOptimalDistance > 25f && !IsHostile()) || mostOptimalDistance > 60f || (targetPlayer != null && targetPlayer.isPlayerDead))
                targetPlayer = null;
            //KeepSearchingForPlayerUnlessInRange(25, ref scoutingSearchRoutine);

            if(currentBehaviourStateIndex != (int)State.ReplicatorStateDocile)
            {
                if(clingingToCeiling)
                {
                    if (!startedCeilingAnimationCoroutine && ceilingAnimationCoroutine == null)
                    {
                        startedCeilingAnimationCoroutine = true;
                        ceilingAnimationCoroutine = StartCoroutine(fallFromCeiling());
                    }
                }
            }

            if(currentBehaviourStateIndex == (int)State.ReplicatorStateActive || currentBehaviourStateIndex == (int)State.ReplicatorStateRoaming || currentBehaviourStateIndex == (int)State.ReplicatorStateFindDocileLocation || currentBehaviourStateIndex == (int)State.ReplicatorStateFleeing)
            {
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            }
            else
            {
                agent.obstacleAvoidanceType = defaultObstacleAvoidanceType;
            }

            bCaresAboutPlayer = false;
            switch (currentBehaviourStateIndex)
            {
                case (int)State.ReplicatorStateSpawnAnim:
                    agent.speed = 0f;
                    //Play unpack anim
                    if (!bPlayedSpawnAnimation)
                    {
                        PlayWakeSFX();
                        //Maybe animation??
                        bPlayedSpawnAnimation = true;
                    }
                    else
                    {
                        PlayWakeSFX();
                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    }
                    break;
                case (int)State.ReplicatorStateFindDocileLocation:
                    agent.speed = 15f;
                    //Find a place to lurk
                    if (DesiredDestination == Vector3.zero || DesiredDestination == null)
                    {
                        //find a place to lurk
                        Transform transform = ChooseClosestNodeToPosition(base.transform.position, avoidLineOfSight: false, offsetNodeAmount);
                        
                        if (transform != null)
                        {
                            DesiredDestination = transform.position;
                            SetTargetDestination(DesiredDestination);
                        }
                        else
                        {
                            transform = ChooseClosestNodeToPosition(base.transform.position);
                            if (transform != null)
                            {
                                DesiredDestination = transform.position;
                                SetTargetDestination(DesiredDestination);
                            }
                            else
                            {
                                //We didn't find a location, so just pick our current as docile
                                SetCurrentBehaviourState(State.ReplicatorStateDocile);
                                bLurking = true;
                            }
                        }
                        //Debug.Log("Picked location " + DesiredDestination + "("+ transform.position + ").. we are at" + base.transform.position+"  ("+ gameObject.transform.position+")");
                    }
                    else
                    {
                        if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                        {
                            //We reached our destination
                            DesiredDestination = Vector3.zero;
                            bLurking = true;
                            SetCurrentBehaviourState(State.ReplicatorStateDocile);
                        }
                        else if (agent.velocity.magnitude < 0.1)
                        {
                            //We are likely fighting another replicator for this, or it is somehow obscured
                            PossiblyStuckTime++;
                            if (PossiblyStuckTime > 5)
                            {
                                DesiredDestination = Vector3.zero;
                            }
                        }
                    }

                    break;
                case (int)State.ReplicatorStateDocile:
                    agent.speed = 0f;
                    bCaresAboutPlayer = true;
                    /*
--If ever set hostile, enter roam
--Find a spot to chill. Usually on a ceiling, or above a door
--Will scan for anything going past. If that something is holding loot, will go to active
                    */
                    //We're passive - lurk for an enemy
                    if (isOutside)
                    {
                        //Shouldn't happen, but just in case we are because of something, go to roaming
                        PlayWakeSFX();
                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    }
                    else
                    {
                        if (IsHostile())
                        {
                            //We were aggroed at some point, don't relax
                            PlayWakeSFX();
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                        }
                        else
                        {
                            if (NearbyTargetWithLoot(true, true))
                            {
                                //If we spot someone wearing loot, go to active
                                SetCurrentBehaviourState(State.ReplicatorStateActive);
                            }
                            else
                            {
                                //Go relax somewhere
                                if (!bLurking)
                                {
                                    SetCurrentBehaviourState(State.ReplicatorStateFindDocileLocation);
                                }
                                else if (!bDesiresCeiling && !bDesiresWall)
                                {
                                    if (!clingingToCeiling && !startedCeilingAnimationCoroutine && ceilingAnimationCoroutine == null)
                                    {
                                        if (RaycastToCeiling())
                                        {
                                            bDesiresCeiling = true;
                                            startedCeilingAnimationCoroutine = true;
                                            DesiresCeilingWallClientRPC(bDesiresCeiling, bDesiresWall, ceilingHidingPoint, DesiredDestination);
                                            ceilingAnimationCoroutine = StartCoroutine(clingToCeiling());
                                        }
                                        else if (GetWallPositionForMesh())
                                        {
                                            bDesiresWall = true;
                                            DesiredDestination = wallPosition;
                                            SetCurrentBehaviourState(State.ReplicatorStateFindDocileLocation, DesiredDestination);
                                        }
                                    }
                                }
                                if (bDesiresCeiling)
                                {
                                    gameObject.transform.position = Vector3.SmoothDamp(gameObject.transform.position, ceilingHidingPoint, ref propelVelocity, 0.1f);
                                    base.transform.eulerAngles = new Vector3(0f, 0f, 180);
                                    inSpecialAnimation = true;
                                    agent.enabled = false;
                                }
                                else if (bDesiresWall)
                                {
                                    gameObject.transform.position = Vector3.SmoothDamp(gameObject.transform.position, wallPosition, ref propelVelocity, 0.1f);

                                    inSpecialAnimation = true;
                                    agent.enabled = false;
                                }
                                //NOTE: Both of above pauses the execution of DoAIInterval. It is restored in Update() if we find a player
                            }
                        }
                    }

                    break;
                case (int)State.ReplicatorStateActive:
                    agent.speed = MatchTargetSpeed(IsHostile());
                    bCaresAboutPlayer = true;

                    /*
--If who activated them is friendly, use them as activated instead, when reaching them, goto roam (+ set my aggro var)
--Will chase whatever activated them
--If encountering scrap, will go to nom
--Will hiss at whatever activated them, and attack them
--Encounter closed door, will go to nom
--Lost target:
---Go to roam
                     */
                    if(ObjectOfInterest != null)
                    {
                        if(!ObjectOfInterest.grabbable || ObjectOfInterest.deactivated)
                        {
                            //We're wanting something we can't have
                            ObjectOfInterest = null;
                        }
                        else
                        {
                            DesiredDestination = ObjectOfInterest.transform.position;
                            if (DesiredDestination != Vector3.zero)
                            {
                                //We were given a place to go - Go there
                                SetTargetDestination(DesiredDestination);
                                if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                                {
                                    //We reached our destination - Eat it!
                                    DesiredDestination = Vector3.zero;
                                    SetCurrentBehaviourState(State.ReplicatorStateNomming);
                                }
                                else if (agent.velocity.magnitude < 0.1)
                                {
                                    //We are likely fighting another replicator for this, or it is somehow obscured
                                    PossiblyStuckTime++;
                                    if (PossiblyStuckTime > 5)
                                    {
                                        DesiredDestination = Vector3.zero;
                                        ObjectOfInterest = null;
                                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                                    }
                                }
                                else
                                {
                                    PossiblyStuckTime = 0;
                                }
                            }
                            else
                            {
                                //should newer be possible, but
                                ObjectOfInterest = null;
                            }
                        }
                    }
                    else if (targetPlayer != null)
                    {
                        //Attack it
                        if((targetPlayer.isInsideFactory && isOutside) || (!targetPlayer.isInsideFactory && !isOutside))
                        {
                            //No way to reach them
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                        }
                        else
                        {
                            DesiredDestination = targetPlayer.transform.position;
                            SetTargetDestination(DesiredDestination);

                            float DistToPlayer = (DesiredDestination - gameObject.transform.position).magnitude;
                            bool InstilFear = true;
                            if (IsHostile())
                            {
                                //We're angy, don't let nothing stop us
                                if (DistToPlayer < 1)
                                {
                                    //We got close to the player, attack them
                                    //TODO: Maybe replace this attack thing
                                    StartCoroutine(SwingAttack());
                                }
                            }
                            else
                            {
                                if(!NearbyTargetWithLoot())
                                {
                                    InstilFear = false;
                                    //Find this scrap, or go to roam if the target got nothing left (If we somehow lost the scrap, don't punish the player)
                                    ObjectOfInterest = TryFindNearbyScrap();
                                    if (ObjectOfInterest != null)
                                    {
                                        //It will be handled in the next loop
                                    }
                                    else if(!NearbyTargetWithLoot(false,true))
                                    {
                                        //The player actually dropped everything, leave it be
                                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                                    }
                                }
                                else
                                {
                                    if (DistToPlayer < 1)
                                    {
                                        //We got close to the player, attack them
                                        //TODO: Maybe replace this attack thing
                                        StartCoroutine(SwingAttack());
                                    }
                                }
                            }
                            if(InstilFear)
                            {
                                if (DistToPlayer < 5)
                                {
                                    GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.5f);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (DesiredDestination != Vector3.zero)
                        {
                            //We were given a place to go - Go there
                            SetTargetDestination(DesiredDestination);
                            if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                            {
                                //We reached our destination
                                DesiredDestination = Vector3.zero;
                                SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                            }
                        }
                        else
                        {
                            //Go to roam
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                        }
                    }
                    break;
                case (int)State.ReplicatorStateNomming:
                    agent.speed = 0f;
                    /*
--Consume what it found
--Depending on the value, replicate n-times
---Pass max replicate -1, and set own counter -1
--If managed to replicate, and never aggroed, and start docile, go to docile
---Otherwise scan, if found someone carrying loot, go to active
----Otherwise, go to roam
---If not managed to replicate (cap), go to defend on this object
                    */
                    //Debug.Log(timeSinceActuallySpawned + ".." + EnteredStateTime + ".." + (timeSinceActuallySpawned - EnteredStateTime));

                    if (stunnedIndefinitely > 0 || stunNormalizedTimer >= 0f)
                    {
                        //We got interrupted eating
                        StopCreatureSFX();
                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    }
                    else if (ObjectOfInterest == null)
                    {
                        //We have somehow lost our object
                        StopCreatureSFX();
                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    }
                    else if (ObjectOfInterest.isPocketed || ObjectOfInterest.isHeld)
                    {
                        //Someone nicked it, get angy
                        bPersonallyAggroed = true;
                        StopCreatureSFX();
                        DoCallout(true);
                        SetCurrentBehaviourState(State.ReplicatorStateActive);
                    }
                    else if (PersonallySpawned >= MyMaxSpawns)
                    {
                        //Defend this scrap - we have no use for it
                        StopCreatureSFX();
                        DoCallout(false); //Tell my friendlies about this place
                        SetCurrentBehaviourState(State.ReplicatorStateDefending, ObjectOfInterest.transform.position);
                    }
                    else if (timeSinceActuallySpawned - EnteredStateTime > 4f)
                    {
                        //Stop FX
                        StopCreatureSFX();
                        LastConsumeSFX = 0;
                        //Actually eat the object
                        EatenObject();
                    }
                    else
                    {
                        ClaimObject(ObjectOfInterest);
                        //Progressively eat it
                        PlayEatingFX();
                        if (ObjectOfInterest != null && (ObjectOfInterest.transform.position - gameObject.transform.position).magnitude > 0.1)
                        {
                            SetTargetDestination(ObjectOfInterest.transform.position);
                        }
                    }

                    break;
                case (int)State.ReplicatorStateReplicating:
                    agent.speed = 0f;
                    if (PersonallySpawned < MyMaxSpawns && CurrentlyConsumedScrap >= ConsumesRessourceValueToReplicate)
                    {
                        //Replicate
                        TryReplicate();
                        //Play replicator FX TODO:
                    }
                    else
                    {
                        //We didn't get enough or are unable to replicate
                        SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    }
                    break;
                case (int)State.ReplicatorStateRoaming:
                    agent.speed = 5f;
                    bCaresAboutPlayer = true;
                    /*
--Roam around the place randomly, scan for scrap
                    */

                    if (DesiredDestination == Vector3.zero)
                    {
                        //Pick new location
                        if(timeSinceActuallySpawned - EnteredStateTime > 45f)
                        {
                            //Been a while since we've found something worthwhile, try pick somewhat close, but not too close
                            DesiredDestination = RoundManager.Instance.GetRandomPositionInRadius(base.transform.position, 45f, 110f);
                            if(DesiredDestination == Vector3.zero)
                                DesiredDestination = RoundManager.Instance.GetRandomPositionInRadius(base.transform.position, 15f, 45f);
                            //Reset timer till next
                            EnteredStateTime = timeSinceActuallySpawned;

                            if (DesiredDestination == Vector3.zero)
                            {
                                //Pick whatever is far from us
                                DesiredDestination = ChooseFarthestNodeFromPosition(base.transform.position, avoidLineOfSight: true, 0, log: true).position;
                            }
                        }
                        //Roam nearby
                        if (DesiredDestination == Vector3.zero)
                            DesiredDestination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(base.transform.position, 35f);
                        if (DesiredDestination == Vector3.zero)
                            DesiredDestination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(base.transform.position, 25f);
                        if (DesiredDestination == Vector3.zero)
                            DesiredDestination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(base.transform.position, 15f);

                        SetTargetDestination(DesiredDestination);
                    }
                    else
                    {
                        if(agent.velocity.magnitude < 0.1)
                        {
                            PossiblyStuckTime++;
                            if(PossiblyStuckTime > 5)
                            {
                                DesiredDestination = Vector3.zero;
                            }
                        }
                        else
                        {
                            PossiblyStuckTime = 0;
                        }
                        if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                        {
                            DesiredDestination = Vector3.zero;
                        }
                        if(ObjectOfInterest)
                        {
                            SetCurrentBehaviourState(State.ReplicatorStateActive);
                        }
                        else
                        {
                            ObjectOfInterest = TryFindNearbyScrap(15, true);
                            if(!ObjectOfInterest)
                            {
                                if(NearbyTargetWithLoot(true, true))
                                {
                                    SetCurrentBehaviourState(State.ReplicatorStateActive);
                                }
                            }
                        }
                    }

                    break;
                case (int)State.ReplicatorStateFleeing:
                    agent.speed = 20f;
                    /*
--Pick a random spot, go there, and enter roam
                    */
                    StopCreatureSFX(false);

                    if (DesiredDestination != Vector3.zero)
                    {
                        //We were given a place to go - Go there
                        SetTargetDestination(DesiredDestination);
                        if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                        {
                            //We reached our destination
                            DesiredDestination = Vector3.zero;
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                        }
                        else if (agent.velocity.magnitude < 0.1)
                        {
                            //We are likely fighting another replicator for this, or it is somehow obscured
                            PossiblyStuckTime++;
                            if (PossiblyStuckTime > 5)
                            {
                                DesiredDestination = Vector3.zero;
                                ObjectOfInterest = null;
                                SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                            }
                        }
                        else
                        {
                            PossiblyStuckTime = 0;
                        }
                    }
                    else
                    {
                        Vector3 PositionToAvoid = gameObject.transform.position;
                        if (targetPlayer != null)
                        {
                            PositionToAvoid = targetPlayer.transform.position;
                        }
                        //Same basic idea as bracken - Might need tweaking?
                        Transform transform = ChooseFarthestNodeFromPosition(PositionToAvoid, avoidLineOfSight: true, 0, log: true);
                        if (transform != null)
                        {
                            DesiredDestination = transform.position;
                        }
                        else
                        {
                            //Failed finding spot - Go roam
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                        }
                    }

                    break;
                case (int)State.ReplicatorStateDefending:
                    agent.speed = 5f;
                    bCaresAboutPlayer = true;
                    /*
--Stay near an object, and attack any player coming near. leash range. If not chasing, and object is missing, act is if you were personally attacked, and set aggrostuff
                    */
                    if (DesiredDestination != Vector3.zero)
                    {
                        //We were given a place to go - Go there
                        SetTargetDestination(DesiredDestination);
                        if ((DesiredDestination - gameObject.transform.position).magnitude < 1)
                        {
                            //We reached our destination
                            DesiredDestination = Vector3.zero;
                        }
                    }
                    else
                    {
                        if (ObjectOfInterest == null) //It disappeared - likely got eaten by a friendly, go to roaming
                            SetCurrentBehaviourState(State.ReplicatorStateRoaming);

                        if (ObjectOfInterest.isHeld || ObjectOfInterest.heldByPlayerOnServer)
                        {
                            //It got picked up - Act as if we were attacked by the offending party
                            GotAttacked(ObjectOfInterest.playerHeldBy);
                        }
                    }

                    break;
                case (int)State.HeadSwingAttackInProgress:
                    // We don't care about doing anything here
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!"+ currentBehaviourStateIndex);
                    SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    break;
            }

            /*switch(currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    agent.speed = 3f;
                    break;
                case (int)State.StickingInFrontOfPlayer:
                    agent.speed = 5f;
                    StickingInFrontOfPlayer();
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }*/
        }


        public void SetTargetDestination(Vector3 destination)
        {
            DesiredDestination = destination;
            if (!agent.enabled)
                return;
            if(!SetDestinationToPosition(DesiredDestination, checkForPath: true))
            {
                LogIfDebugBuild("Failed finding a path to "+DesiredDestination+".."+ offsetNodeAmount);
                if(ObjectOfInterest != null)
                {
                    LogIfDebugBuild("Trying to path to " + ObjectOfInterest.name);
                }
                if(targetPlayer != null)
                {
                    LogIfDebugBuild("Interested in " + targetPlayer.name);
                }
                DesiredDestination = Vector3.zero;
                offsetNodeAmount++;
            }
        }

        /*
Events: 
-Got attacked (If survived)
--Tell my "creator"
--Wail out to any nearby replicators, and trigger active on them
--Check for "friendly nearby replicators", if alone, go to flee, otherwise go to active
--Set my aggro variable
        */
        public void GotAttacked(PlayerControllerB attacker = null)
        {
            if (isEnemyDead)
                return;

            int FoundFriendlies;
            //Aggro myself
            bPersonallyAggroed = true;

            FoundFriendlies = DoCallout(true);

            LogIfDebugBuild("Replicator got attacked");
            //Go to state
            if (FoundFriendlies == 0)
            {
                //Flee
                SetCurrentBehaviourState(State.ReplicatorStateFleeing);
            }
            else
            {
                if(attacker != null)
                {
                    targetPlayer = attacker;
                }
                SetCurrentBehaviourState(State.ReplicatorStateActive);
            }
        }

        public int DoCallout(bool bAngy)
        {
            int FoundFriendlies = 0;

            if (stunnedIndefinitely > 0 || stunNormalizedTimer >= 0f)
            {
                //If we are stunned, don't call for help
                return 0;
            }

            //Tell my parent
            if (Parent != null)
            {
                Parent.ReceiveCallout(this, bAngy);
            }
            LogIfDebugBuild("Replicator doing callout - Angy? " + bAngy);

            //Tell any replicator nearby
            Collider[] results = Physics.OverlapSphere(base.transform.position, FriendlyAggroRange, EnemyMask);
            ReplicatorEnemyAI tc = null;
            //Debug.Log(results.Length);
            for (int i = 0; i < results.Length; i++)
            {
                //Debug.Log(results[i].gameObject);
                if (results[i] == null)
                    continue;
                tc = results[i].gameObject.GetComponent<ReplicatorEnemyAI>();
                if (tc == null)
                    tc = results[i].gameObject.GetComponentInParent<ReplicatorEnemyAI>();
                if(tc == null)
                    continue;
                if (tc == this)
                    continue;
                tc.ReceiveCallout(this, bAngy);
                FoundFriendlies++;
            }

            //Do soundFX
            PlayAlertSFX();

            return FoundFriendlies;
        }

        public void ReceiveCallout(ReplicatorEnemyAI source, bool bAngy = false)
        {
            if (source == null)
                return;

            LogIfDebugBuild("Replicator received callout - Angy? " + bAngy);
            if (!bGroupAggroed)
                bGroupAggroed = bAngy;

            SetCurrentBehaviourState(State.ReplicatorStateRoaming, source.transform.position);
        }
        private IEnumerator clingToCeiling()
        {
            yield return new WaitForSeconds(0.52f);
            if (currentBehaviourStateIndex != (int)State.ReplicatorStateDocile)
            {
                clingingToCeiling = false;
                startedCeilingAnimationCoroutine = false;
            }
            else
            {
                clingingToCeiling = true;
                ceilingAnimationCoroutine = null;
                startedCeilingAnimationCoroutine = false;
            }
        }

        private IEnumerator fallFromCeiling()
        {
            targetNode = null;
            Vector3 startPosition = base.transform.position;
            Vector3 groundPosition = base.transform.position;
            ray = new Ray(base.transform.position, Vector3.down);
            if (Physics.Raycast(ray, out rayHit, 20f, 268435712))
            {
                groundPosition = rayHit.point;
            }
            else
            {
                Debug.LogError("Replicator: I could not get a raycast to the ground after falling from the ceiling! Choosing the closest nav mesh position to self.");
                startPosition = RoundManager.Instance.GetNavMeshPosition(ray.GetPoint(4f), default(NavMeshHit), 7f);
                if (base.IsOwner && !RoundManager.Instance.GotNavMeshPositionResult)
                {
                    KillEnemyOnOwnerClient(overrideDestroy: true);
                }
            }
            /*float fallTime = 0f;
            while (fallTime < 0.1f)
            {
                yield return null;
                fallTime += Time.deltaTime * 2.5f;
                base.transform.position = Vector3.Lerp(startPosition, groundPosition, fallToGroundCurve.Evaluate(fallTime));
            }*/
            //TODO: Maybe sound
            if (hitGroundSFX != null)
                PlayHitGroundSFX();
            float distToPlayer = Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, base.transform.position);
            if (distToPlayer < 13f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            }
            serverPosition = base.transform.position;
            if (base.IsOwner)
            {
                agent.speed = 0f;
            }
            else
            {
                base.transform.eulerAngles = new Vector3(0f, base.transform.eulerAngles.y, 0f);
            }
            clingingToCeiling = false;
            inSpecialAnimation = false;
            yield return new WaitForSeconds(0.5f);
            //RoundManager.PlayRandomClip(creatureSFX, shriekClips);
            if (distToPlayer < 7f)
            {
                GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.5f);
            }
            ceilingAnimationCoroutine = null;
            startedCeilingAnimationCoroutine = false;
        }

        private bool RaycastToCeiling()
        {
            ray = new Ray(base.transform.position, Vector3.up);
            if (Physics.Raycast(ray, out rayHit, 20f, 256))
            {
                //TODO: See if we can optimize this to make sure it is IN BOUNDS (Some rooms, it seems to end up "ontop" - just visually problematic
                ceilingHidingPoint = ray.GetPoint(rayHit.distance + 0.3f);
                ceilingHidingPoint = RoundManager.Instance.RandomlyOffsetPosition(ceilingHidingPoint, 0.25f);
                return true;
            }
            else
            {
                offsetNodeAmount++;
                targetNode = null;
                LogIfDebugBuild("Raycast to ceiling failed. Setting different node offset and resuming search for a hiding spot.");
                return false;
            }
        }


        private bool GetWallPositionForMesh()
        {
            float num = 6f;
            if (Physics.Raycast(base.transform.position, Vector3.up, out rayHit, 22f, StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
            {
                num = rayHit.distance - 1.3f;
            }
            float num2 = RoundManager.Instance.YRotationThatFacesTheNearestFromPosition(base.transform.position + Vector3.up * num, 10f);
            if (num2 != -777f)
            {
                turnCompass.eulerAngles = new Vector3(0f, num2, 0f);
                ray = new Ray(base.transform.position + Vector3.up * num, turnCompass.forward);
                if (Physics.Raycast(ray, out rayHit, 10.1f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    wallPosition = ray.GetPoint(rayHit.distance - 0.2f);
                    wallNormal = rayHit.normal;
                    if (Physics.Raycast(wallPosition, Vector3.down, out rayHit, 7f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        DesiredDestination = rayHit.point;
                        return true;
                    }
                }
            }
            return false;
        }

        void EatenObject()
        {
            //Try to consume the object
            if (ObjectOfInterest != null)
            {
                //We have one. Remove it and go to replication
                ObjectOfInterest.deactivated = true;
                ObjectOfInterest.enabled = false;
                if(ObjectOfInterest.scrapValue > ConsumesRessourceValueToReplicate *2)
                    CurrentlyConsumedScrap += ConsumesRessourceValueToReplicate * 2; //Cap it to max 2 spawns
                else if (ObjectOfInterest.scrapValue > 0)
                    CurrentlyConsumedScrap += ObjectOfInterest.scrapValue;
                else
                    CurrentlyConsumedScrap += 1; //Atleast 1 value if we bothered eating it
                LogIfDebugBuild("Replicator consumed scrap "+ObjectOfInterest+".."+ObjectOfInterest.gameObject.name+" with a value of "+ ObjectOfInterest.scrapValue+". We now have a stomach full of "+ CurrentlyConsumedScrap);
                try
                {
                    Destroy(ObjectOfInterest.gameObject);
                }
                catch(System.Exception e) {
                    Debug.Log("error destroying obj - " + e);
                };
                ObjectOfInterest = null;
                SetCurrentBehaviourState(State.ReplicatorStateReplicating);
                if (Poof != null)
                {
                    Poof.Play();
                }
            }
            else
            {
                //We should never be in here without an object, but go to roaming then
                SetCurrentBehaviourState(State.ReplicatorStateRoaming);
            }
        }

        void KeepSearchingForPlayerUnlessInRange(float range, ref AISearchRoutine routine){
            
            if (false && targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) <= range)
            {
                if(routine.inProgress){
                    LogIfDebugBuild("Start Target Player");
                    StopSearch(routine);
                    SetCurrentBehaviourState(State.StickingInFrontOfPlayer);
                }
            }
            else
            {
                if(!routine.inProgress){
                    LogIfDebugBuild("Stop Target Player");
                    StartSearch(transform.position, routine);
                    SetCurrentBehaviourState(State.SearchingForPlayer);
                }
            }
        }

        void StickingInFrontOfPlayer(){
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }
            if(timeSinceNewRandPos > 0.7f){
                timeSinceNewRandPos = 0;
                if(enemyRandom.Next(0, 5) == 0){
                    // Attack
                    StartCoroutine(SwingAttack());
                }
                else{
                    // Go in front of player
                    positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));
                    StalkPos = targetPlayer.transform.position - Vector3.Scale(new Vector3(-5, 0, -5), targetPlayer.transform.forward) + positionRandomness;
                }
                SetTargetDestination(StalkPos);
            }
        }

        IEnumerator SwingAttack(){
            if (targetPlayer != null)
            {
                SetCurrentBehaviourState(State.HeadSwingAttackInProgress);
                StalkPos = targetPlayer.transform.position;
                SetTargetDestination(StalkPos);
                yield return new WaitForSeconds(0.5f);
                if (isEnemyDead)
                {
                    SetCurrentBehaviourState(State.ReplicatorStateRoaming);
                    yield break;
                }
                PlayAttackFX();
                DoAnimationClientRpc("swingAttack");
                yield return new WaitForSeconds(0.24f);
                SwingAttackHitClientRpc();
                // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
                if (currentBehaviourStateIndex != (int)State.HeadSwingAttackInProgress)
                {
                    yield break;
                }
                SetCurrentBehaviourState(State.ReplicatorStateActive);
            }
            else
            {
                SetCurrentBehaviourState(State.ReplicatorStateRoaming);
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Replicator Enemy Collision with Player!");
                PlayAttackFX();
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(GetDamageForAction(false, true));
                GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.5f);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if(isEnemyDead)
            {
                StartCoroutine(DoDeath());
                return;
            }
            enemyHP -= force;
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.
                    StopCoroutine(SwingAttack());
                    StartCoroutine(DoDeath());
                    KillEnemyOnOwnerClient();
                }
                else
                {
                    GotAttacked(playerWhoHit);
                }
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SwingAttackHitClientRpc()
        {
            LogIfDebugBuild("SwingAttackHitClientRPC");
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if(hitColliders.Length > 0){
                foreach (var player in hitColliders){
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild("Swing attack hit player!");
                        timeSinceHittingLocalPlayer = 0f;
                        playerControllerB.DamagePlayer(GetDamageForAction(true));
                    }
                }
            }
        }


        public void MoveLegsProcedurallyNew()
        {
            for (int i = 0; i < legTargets.Length; i++)
            {
                MoveLegsProcedurallyDetailed(i);
            }
        }

        public void MoveLegsProcedurallyDetailed(int i)
        {
            if (i >= legTargets.Length || i < 0)
                return;

            legTargets[i].position = legPositions[i];
            legTargets[i].up = currentLegNormal[i];
            bool flag = false;


            Vector3 refVelOrig = /*gameObject.transform.InverseTransformDirection(*/Vector3.Normalize(agent.desiredVelocity)/*)*/;

            /*refVelOrigToTest.x *= -1;
            refVelOrigToTest.z *= -1;*/

            if ((legPositions[i] - legDefaultPositions[i].position).sqrMagnitude > legDistances[i] * 1.4f)
            {
                Vector3 refVel;
                float modifier = 0.3f;
                refVel = refVelOrig;
                if (refVel.x > legDistances[i])
                    refVel.x = legDistances[i];
                if (refVel.y > legDistances[i])
                    refVel.y = legDistances[i];
                if (refVel.z > legDistances[i])
                    refVel.z = legDistances[i];
                if (refVel.x < -legDistances[i])
                    refVel.x = -legDistances[i];
                if (refVel.y < -legDistances[i])
                    refVel.y = -legDistances[i];
                if (refVel.z < -legDistances[i])
                    refVel.z = -legDistances[i];
                if (i > 1)
                    modifier = 1.3f;
                else
                    modifier = 1.6f;
                refVel = legDefaultPositions[i].position + (refVel * modifier);


                Ray ray = new Ray(refVel, Vector3.down);


                if (Physics.Raycast(ray, out RaycastHit info, 10, terrainLayer.value))
                {
                    //Debug.Log("Raycasted" + i + ".." + legDistances[i] + ".." + isOtherFootMoving(i) + ".." + legLerp[i]);
                    if (/*Vector3.Distance(newLegPositions[i], info.point) > legDistances[i] * 1.4f && */!isOtherFootMoving(i) && legLerp[i] >= 1)
                    {
                        legLerp[i] = 0;
                        //int direction = legTargets[i].InverseTransformPoint(info.point).z > legTargets[i].InverseTransformPoint(newLegPositions[i]).z ? 1 : -1;
                        newLegPositions[i] = info.point;
                        newLegNormal[i] = info.normal;
                        flag = true;
                        //Debug.Log("Found new position for " + i);
                    }
                }
            }

            if (legLerp[i] < 1)
            {
                //Debug.Log("Lerping " + i);
                Vector3 tempPosition = Vector3.Lerp(oldLegPositions[i], newLegPositions[i], legLerp[i]);
                tempPosition.y += Mathf.Sin(legLerp[i] * Mathf.PI) * stepHeight;

                legPositions[i] = tempPosition;
                //currentLegNormal[i] = Vector3.Lerp(oldLegNormal[i], newLegNormal[i], legLerp[i]);
                legLerp[i] += Time.deltaTime * 30f;
            }
            else
            {
                oldLegPositions[i] = newLegPositions[i];
                oldLegNormal[i] = newLegNormal[i];
            }
            if (flag)
            {
                //Debug.Log("FootStep for " + i);
                footstepAudio.pitch = Random.Range(0.6f, 1.2f);
                footstepAudio.PlayOneShot(footstepSFX[Random.Range(0, footstepSFX.Length)], Random.Range(0.1f, 1f));
                WalkieTalkie.TransmitOneShotAudio(footstepAudio, footstepSFX[Random.Range(0, footstepSFX.Length)], Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            }
        }

        public void MoveLegsProcedurally()
        {

            Vector3 refVelOrig = Vector3.Normalize(agent.velocity);
            Vector3 refVel;
            float modifier = 0.3f;
            for (int i = 0; i < legTargets.Length; i++)
            {
                refVel = refVelOrig;
                if (refVel.x > legDistances[i])
                    refVel.x = legDistances[i];
                if (refVel.y > legDistances[i])
                    refVel.y = legDistances[i];
                if (refVel.z > legDistances[i])
                    refVel.z = legDistances[i];
                if (refVel.x < -legDistances[i])
                    refVel.x = -legDistances[i];
                if (refVel.y < -legDistances[i])
                    refVel.y = -legDistances[i];
                if (refVel.z < -legDistances[i])
                    refVel.z = -legDistances[i];
                if (i > 1)
                    modifier = 0.1f;
                else
                    modifier = 0.2f;
                legLerp[i] += Time.deltaTime * 14f;
                Vector3 tempPosition = Vector3.Lerp(legTargets[i].position + (refVel * modifier), legPositions[i], 14f * Time.deltaTime);

                if (legLerp[i] < 1)
                {
                    //Debug.Log("Lerping " + i);
                    tempPosition.y += Mathf.Sin(legLerp[i] * Mathf.PI) * (stepHeight / 2);
                }
                legTargets[i].position = tempPosition;
            }
            float BonusLegDist = 1.4f;
            float BonusBonusLegDist = 0f;
            if (refVelOrig.magnitude > 0.2)
            {
                BonusBonusLegDist = 5f;
            }
            bool flag = false;
            for (int j = 0; j < legPositions.Length; j++)
            {
                if (!isOtherFootMoving(j) && (legPositions[j] - legDefaultPositions[j].position).sqrMagnitude > legDistances[j] * BonusLegDist + BonusBonusLegDist * legDistances[j])
                {
                    legPositions[j] = legDefaultPositions[j].position;
                    legLerp[j] = 0;
                    flag = true;
                }
            }
            if (flag)
            {
                footstepAudio.pitch = Random.Range(0.8f, 1.1f);
                footstepAudio.PlayOneShot(footstepSFX[Random.Range(0, footstepSFX.Length)], Random.Range(0.7f, 1f));
                WalkieTalkie.TransmitOneShotAudio(footstepAudio, footstepSFX[Random.Range(0, footstepSFX.Length)], Mathf.Clamp(Random.Range(-0.4f, 0.8f), 0f, 1f));
            }
        }

        public bool isOtherFootMoving(int i)
        {
            if (i >= legTargets.Length || i < 0)
                return false;

            if (i == 0)
                i = 1;
            else if (i == 1)
                i = 0;
            else if (i == 2)
                i = 3;
            else if (i == 3)
                i = 2;

            return legLerp[i] < 1;
        }
    }
}