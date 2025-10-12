using UnityEngine;

namespace CS.Base
{
    [RequireComponent(typeof(NetworkTransformSync))]
    [AddComponentMenu("ChronoSync/Compat/CronosyncTransformView")]
    public class CronosyncTransformView : MonoBehaviour
    {
        private NetworkTransformSync nts;

        private void Awake()
        {
            nts = GetComponent<NetworkTransformSync>();
            if (nts.target == null) nts.target = transform;
        }

        public void ApplyIdentity(string entityId, bool isMine)
        {
            if (nts == null) nts = GetComponent<NetworkTransformSync>();
            nts.SetEntityId(entityId);
            nts.isLocalAuthority = isMine;
        }
    }
}
