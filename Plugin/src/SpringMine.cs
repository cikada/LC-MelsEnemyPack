using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using System.Collections;
using GameNetcodeStuff;
using UnityEngine;

namespace MelsEnemyPack.src
{
    class SpringMine : NetworkBehaviour, IHittable
    {
        private void Start()
        {
            Debug.Log("SPawning mine ! ===");
        }

        void LogIfDebugBuild(string text)
        {
#if DEBUG
            Plugin.Logger.LogInfo(text);
#endif
        }
        public bool Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            /*SetOffMineAnimation();
            sendingExplosionRPC = true;
            ExplodeMineServerRpc();*/
            return true;
        }
    }
}
