using UnityEngine;

namespace CS.Core.Systems
{
    // Minimal camera follow/look component used when Cinemachine is not installed.
    [RequireComponent(typeof(Camera))]
    public class SimpleFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Transform lookAt;
        [Header("Offsets")]
        public Vector3 positionOffset = new Vector3(0, 3.5f, -5.5f);
        public Vector3 lookOffset = new Vector3(0, 1.6f, 0);
        [Header("Smoothing")]
        public float positionLerp = 8f;
        public float rotationLerp = 10f;

        void LateUpdate()
        {
            if (target == null)
                return;
            var desiredPos = target.position + target.TransformVector(positionOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-positionLerp * Time.deltaTime));

            var focus = lookAt != null ? lookAt.position + lookOffset : target.position + lookOffset;
            var desiredRot = Quaternion.LookRotation((focus - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationLerp * Time.deltaTime));
        }
    }
}
