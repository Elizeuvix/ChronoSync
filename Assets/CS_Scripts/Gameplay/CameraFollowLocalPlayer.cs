using UnityEngine;
using CS.Core.Identity;

public class CameraFollowLocalPlayer : MonoBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("Velocidade de suavização do seguimento (maior = responde mais rápido)")]
    public float smoothSpeed = 6f;
    [Tooltip("Velocidade de suavização da rotação (maior = responde mais rápido)")]
    public float rotationSmoothSpeed = 10f;
    [Tooltip("Offset relativo ao alvo (em espaço local do player)")]
    public Vector3 offset = new Vector3(0, 2.0f, -5f);
    [Tooltip("Distância para olhar à frente do player")]
    public float lookAhead = 6f;
    [Tooltip("Se true, usa o offset no espaço local do alvo; caso false, offset em espaço mundial")]
    public bool offsetIsLocal = true;

    private Transform cameraTarget;   // Geralmente o filho "CameraTarget" do Player
    private bool targetFound = false;

    void Start()
    {
        FindLocalPlayer();
    }

    void LateUpdate()
    {
        if (!targetFound)
        {
            FindLocalPlayer();
            return;
        }

        if (cameraTarget == null) return;

        // 1) Posição desejada (atrás/acima do player). Se offset for local, segue a rotação do player
        Vector3 desiredPosition = offsetIsLocal ? cameraTarget.TransformPoint(offset) : cameraTarget.position + offset;
        float posT = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime); // suavização independente do frame rate
        transform.position = Vector3.Lerp(transform.position, desiredPosition, posT);

        // 2) Olhar para frente do player (um ponto à frente da cabeça/CameraTarget)
        Vector3 lookPoint = cameraTarget.position + cameraTarget.forward * Mathf.Max(0.01f, lookAhead);
        Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        float rotT = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);
    }

    private void FindLocalPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            var identity = player.GetComponent<PlayerIdentity>();
            if (identity != null && identity.IsLocal)
            {
                Transform target = player.transform.Find("CameraTarget");
                if (target == null) target = player.transform; // fallback: usa o root do player

                cameraTarget = target;
                targetFound = true;
                Debug.Log("[CameraFollow] Focando no Player local: " + player.name);
                break;
            }
        }
    }
}
