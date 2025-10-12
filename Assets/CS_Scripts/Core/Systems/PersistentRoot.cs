using UnityEngine;

namespace CS.Core.Systems
{
    // Ensure this runs very early so the object is marked as persistent
    [DefaultExecutionOrder(-10000)]
    public sealed class PersistentRoot : MonoBehaviour
    {
        private static PersistentRoot _instance;

        [SerializeField]
        [Tooltip("If true, this GameObject will be kept between scene loads.")]
        private bool keepBetweenScenes = true;

        [SerializeField]
        [Tooltip("If true, destroys duplicate instances at runtime.")]
        private bool enforceSingleton = true;

        private void Awake()
        {
            if (enforceSingleton)
            {
                if (_instance != null && _instance != this)
                {
                    // Another instance already exists; destroy this duplicate
                    Destroy(gameObject);
                    return;
                }
                _instance = this;
            }

            if (keepBetweenScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}
