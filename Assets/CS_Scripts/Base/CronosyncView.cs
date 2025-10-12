using UnityEngine;

namespace CS.Base
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ChronoSync/Compat/CronosyncView")]
    public class CronosyncView : MonoBehaviour
    {
        [Header("Identity")] public string entityId;
        [Tooltip("True if this client controls this entity")] public bool isMine;

        [Header("Observables")] public CronosyncTransformView transformView;
        public CronosyncAnimatorView animatorView;
        public CronosyncRigidbodyView rigidbodyView;

        private void Reset()
        {
            transformView = GetComponent<CronosyncTransformView>();
            animatorView = GetComponent<CronosyncAnimatorView>();
            rigidbodyView = GetComponent<CronosyncRigidbodyView>();
        }

        private void Awake()
        {
            TryRegister();
        }

        private void OnDestroy()
        {
            Unregister();
        }

        public void SetIdentity(string id, bool mine)
        {
            entityId = id; isMine = mine;
            if (transformView != null) transformView.ApplyIdentity(id, mine);
            if (animatorView != null) animatorView.ApplyIdentity(id, mine);
            if (rigidbodyView != null) rigidbodyView.ApplyIdentity(id, mine);
            TryRegister();
        }

        private void TryRegister()
        {
            if (!string.IsNullOrEmpty(entityId))
            {
                Registry[entityId] = this;
            }
        }

        private void Unregister()
        {
            if (!string.IsNullOrEmpty(entityId) && Registry.TryGetValue(entityId, out var v) && v == this)
            {
                Registry.Remove(entityId);
            }
        }

        public static readonly System.Collections.Generic.Dictionary<string, CronosyncView> Registry = new System.Collections.Generic.Dictionary<string, CronosyncView>();
        public static bool TryGet(string id, out CronosyncView view) => Registry.TryGetValue(id, out view);
    }
}
