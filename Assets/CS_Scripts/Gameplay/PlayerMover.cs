using UnityEngine;
using UnityEngine.AI;
using CS.Core.Identity;

[RequireComponent(typeof(NavMeshAgent))]
public class PlayerMover : MonoBehaviour
{
    public float moveSpeed = 5f; // aplicado em agent.speed
    public bool useKeyboard = true;
    public bool useClickToMove = true;
    [Tooltip("Tolerância adicional para considerar chegada ao destino")] public float arrivalTolerance = 0.1f;

    private NavMeshAgent agent;
    private Camera mainCamera;
    private PlayerIdentity identity;
    [SerializeField] private Animator animator;
    [SerializeField] private string walkingBoolName = "Walking";
    private bool movingToClick = false;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        mainCamera = Camera.main;
        identity = GetComponent<PlayerIdentity>();
        UpdateAnimator();

        // Se não for o player local, desativa o controle
        if (identity != null && !identity.IsLocal)
        {
            enabled = false;
            return;
        }

        // Garante que o agente comece parado
        agent.updateRotation = false;
        agent.updateUpAxis = true;
        agent.isStopped = true;
        agent.speed = moveSpeed;
    }

    public void UpdateAnimator()
    {
        animator = GetComponentInChildren<Animator>();
    }
    
    public void SetAnimationState(string stateName, bool value)
    {
        if (animator != null)
        {
            animator.SetBool(stateName, value);
        }
    }

    private void Update()
    {
        // Atualiza referência ao Animator se faltar (por segurança em prefabs dinâmicos)
        if (animator == null) UpdateAnimator();
        if (useClickToMove)
            HandleClickToMove();

        if (useKeyboard)
            HandleKeyboardMovement();

        // Se estiver em movimento, rotaciona suavemente na direção do destino
        if (agent.velocity.sqrMagnitude > 0.1f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(agent.velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
        }

        // Detecta chegada quando usando click-to-move e controla animação
        bool arrived = false;
        if (!agent.pathPending)
        {
            bool closeEnough = agent.remainingDistance <= (agent.stoppingDistance + arrivalTolerance);
            bool nearlyStopped = !agent.hasPath || agent.velocity.sqrMagnitude < 0.001f;
            arrived = closeEnough && nearlyStopped;
        }
        if (movingToClick && arrived)
        {
            movingToClick = false;
            agent.isStopped = true; // respeita stoppingDistance
        }

        // Controla a animação Walking (verdadeiro quando se move, falso ao chegar)
        bool isWalking = !arrived && agent.velocity.sqrMagnitude > 0.001f;
        if (animator != null)
        {
            animator.SetBool(walkingBoolName, isWalking);
            // mantém compatibilidade com um possível parâmetro "Stopped"
            animator.SetBool("Stopped", !isWalking);
        }
    }

    private void HandleClickToMove()
    {
        if (Input.GetMouseButtonDown(1)) // Botão direito do mouse
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                {
                    agent.ResetPath();
                    agent.isStopped = false;
                    agent.speed = moveSpeed;
                    agent.SetDestination(navHit.position);
                    movingToClick = true;
                }
            }
        }
    }

    private void HandleKeyboardMovement()
    {
        float h = Input.GetAxis("Horizontal"); // A/D ou ← →
        float v = Input.GetAxis("Vertical");   // W/S ou ↑ ↓

        Vector3 direction = new Vector3(h, 0, v).normalized;

        if (direction.magnitude > 0.1f)
        {
            // Cancela o caminho de click-to-move e usa deslocamento via NavMeshAgent
            movingToClick = false;
            agent.ResetPath();
            agent.isStopped = false;
            agent.speed = moveSpeed;
            agent.Move(direction * moveSpeed * Time.deltaTime);

            // Rotaciona suavemente na direção do movimento
            Quaternion toRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, toRotation, Time.deltaTime * 10f);
        }
    }
}

