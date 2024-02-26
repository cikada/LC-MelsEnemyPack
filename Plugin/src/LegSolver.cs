using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements.Experimental;

namespace MelsEnemyPack
{

    // You may be wondering, how does the Example Enemy know it is from class MelsEnemyPackAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class LegSolver : MonoBehaviour
    {
        [SerializeField] LayerMask terrainLayer = default;
        [SerializeField] Transform body = default;
        [SerializeField] LegSolver otherFoot = default;
        [SerializeField] float speed = 1;
        [SerializeField] float stepDistance = 4;
        [SerializeField] float stepLength = 4;
        [SerializeField] float stepHeight = 1;
        [SerializeField] Vector3 footOffset = default;
        float footSpacing;
        Vector3 oldPosition, currentPosition, newPosition;
        Vector3 oldNormal, currentNormal, newNormal;
        float lerp;

        private void Start()
        {
            footSpacing = transform.localPosition.x;
            currentPosition = newPosition = oldPosition = transform.position;
            currentNormal = newNormal = oldNormal = transform.up;
            lerp = 1;
        }

        // Update is called once per frame

        void Update()
        {
            transform.position = currentPosition;
            transform.up = currentNormal;

            Ray ray = new Ray(body.position + (body.right * footSpacing), Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit info, 10, terrainLayer.value))
            {
                if (Vector3.Distance(newPosition, info.point) > stepDistance && !otherFoot.IsMoving() && lerp >= 1)
                {
                    lerp = 0;
                    int direction = body.InverseTransformPoint(info.point).z > body.InverseTransformPoint(newPosition).z ? 1 : -1;
                    newPosition = info.point + (body.forward * stepLength * direction) + footOffset;
                    newNormal = info.normal;
                }
            }

            if (lerp < 1)
            {
                Vector3 tempPosition = Vector3.Lerp(oldPosition, newPosition, lerp);
                tempPosition.y += Mathf.Sin(lerp * Mathf.PI) * stepHeight;

                currentPosition = tempPosition;
                currentNormal = Vector3.Lerp(oldNormal, newNormal, lerp);
                lerp += Time.deltaTime * speed;
            }
            else
            {
                oldPosition = newPosition;
                oldNormal = newNormal;
            }
        }

        private void OnDrawGizmos()
        {

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(newPosition, 0.5f);
        }



        public bool IsMoving()
        {
            return lerp < 1;
        }
    }
}