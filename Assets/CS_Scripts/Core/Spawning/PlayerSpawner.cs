using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using CS.Core.Identity;
#if UNITY_STANDARD_ASSETS
using UnityStandardAssets.Characters.ThirdPerson;
#endif
using CS.Base;
using CS.Core.Networking;

namespace CS.Core.Spawning
{
    [AddComponentMenu("ChronoSync/Player Spawner")]
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject playerPrefab;

        [Header("Spawn Points")] 
        [Tooltip("Optional fixed spawn points. If empty, will use this object's transform or a random nearby NavMesh point.")]
        public Transform[] spawnPoints;
        [Tooltip("If true, pick a random spawn point; otherwise uses the first.")]
        public bool randomizeSpawnPoint = true;

        [Header("Grounding/Placement")] 
        [Tooltip("Project downwards to ground via raycast before placing the player.")]
        public bool raycastToGround = true;
        public float raycastMaxDistance = 10f;
        [Tooltip("Try to snap to nearest NavMesh position.")]
        public bool snapToNavMesh = true;
        public float navMeshSampleMaxDistance = 3f;
        public NavMeshQueryFilter navMeshFilter;

        private readonly Dictionary<string, GameObject> _spawned = new();

        void Reset()
        {
            navMeshFilter = new NavMeshQueryFilter { areaMask = NavMesh.AllAreas, agentTypeID = 0 };
        }

        public GameObject SpawnLocal(string playerId, string nickname, Team team)
        {
            return Spawn(playerId, nickname, team, isLocal: true);
        }

        public GameObject SpawnRemote(string playerId, string nickname, Team team)
        {
            return Spawn(playerId, nickname, team, isLocal: false);
        }

        public bool Despawn(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            if (_spawned.TryGetValue(playerId, out var go) && go != null)
            {
                Destroy(go);
                _spawned.Remove(playerId);
                return true;
            }
            return false;
        }

        public bool TryGetSpawned(string playerId, out GameObject go)
        {
            return _spawned.TryGetValue(playerId, out go);
        }

        // Permite renomear a chave de um jogador já instanciado (ex.: id provisório -> id definitivo do servidor)
        public bool RenameSpawnedId(string oldPlayerId, string newPlayerId)
        {
            if (string.IsNullOrWhiteSpace(oldPlayerId) || string.IsNullOrWhiteSpace(newPlayerId)) return false;
            if (oldPlayerId == newPlayerId) return true;
            if (!_spawned.TryGetValue(oldPlayerId, out var go) || go == null) return false;
            // Se já existir a nova chave, não sobrescrever
            if (_spawned.ContainsKey(newPlayerId)) return false;
            _spawned.Remove(oldPlayerId);
            _spawned[newPlayerId] = go;
            return true;
        }

        private GameObject Spawn(string playerId, string nickname, Team team, bool isLocal)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("PlayerSpawner: playerPrefab não atribuído.");
                return null;
            }

            if (_spawned.ContainsKey(playerId))
            {
                Debug.LogWarning($"PlayerSpawner: playerId '{playerId}' já instanciado. Ignorando spawn duplicado.");
                return _spawned[playerId];
            }

            var (pos, rot) = GetSpawnTransform();

            // Ajuste de solo/NavMesh
            pos = AdjustPositionToGround(pos);
            if (snapToNavMesh && NavMesh.SamplePosition(pos, out var hit, navMeshSampleMaxDistance, navMeshFilter))
            {
                pos = hit.position;
            }

            var instance = Instantiate(playerPrefab, pos, rot);

            // Identidade
            var identity = instance.GetComponentInChildren<PlayerIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<PlayerIdentity>();
            }
            identity.SetAll(playerId, nickname, team, isLocal);

            // Garante NetworkTransformSync para locais e remotos
            var nts = instance.GetComponentInChildren<NetworkTransformSync>();
            if (nts == null)
            {
                nts = instance.AddComponent<NetworkTransformSync>();
            }
            nts.SetEntityId(playerId);
            nts.isLocalAuthority = isLocal;
            if (nts.target == null) nts.target = instance.transform;

            // Configura CronosyncView (Photon-like) e registra para roteamento de eventos
            var csv = instance.GetComponentInChildren<CronosyncView>();
            if (csv != null)
            {
                csv.SetIdentity(playerId, isLocal);
            }

            // Dirigir animação de remotos baseado em movimento
            if (!isLocal)
            {
                if (instance.GetComponentInChildren<RemoteAnimatorDriver>() == null)
                {
                    instance.AddComponent<RemoteAnimatorDriver>();
                }
            }

            // Standard Assets: somente o player local deve ler input
            // Standard Assets (opcionais)
#if UNITY_STANDARD_ASSETS
            var tpUserCtrl = instance.GetComponentInChildren<ThirdPersonUserControl>();
            if (tpUserCtrl != null) tpUserCtrl.enabled = isLocal;
            var aiCtrl = instance.GetComponentInChildren<AICharacterControl>();
            if (aiCtrl != null) aiCtrl.enabled = !isLocal;
#endif

            // Se tiver NavMeshAgent, opcionalmente força warp para garantir posicionamento correto
            var agent = instance.GetComponentInChildren<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh)
            {
                agent.Warp(pos);
            }

            _spawned[playerId] = instance;
            return instance;
        }

        // Remove todos os jogadores, exceto o informado (ex.: manter o local ao fechar lobby remotos)
        public void DespawnAllExcept(string keepPlayerId)
        {
            var toRemove = new List<string>();
            foreach (var kv in _spawned)
            {
                if (!string.Equals(kv.Key, keepPlayerId, System.StringComparison.Ordinal))
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                _spawned.Remove(toRemove[i]);
            }
        }

        private (Vector3 pos, Quaternion rot) GetSpawnTransform()
        {
            Transform t = null;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                t = randomizeSpawnPoint ? spawnPoints[Random.Range(0, spawnPoints.Length)] : spawnPoints[0];
            }
            if (t == null) t = transform;
            return (t.position, t.rotation);
        }

        private Vector3 AdjustPositionToGround(Vector3 pos)
        {
            if (!raycastToGround) return pos;
            var origin = pos + Vector3.up * (raycastMaxDistance * 0.5f);
            if (Physics.Raycast(origin, Vector3.down, out var hit, raycastMaxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            return pos;
        }
    }
}
