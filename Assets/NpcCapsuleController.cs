using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NPC com patrulha, perseguicao e conversa, movido SEMPRE por NavMeshAgent
/// (determinístico, funciona sem internet). O LLM so sugere uma intencao; quem decide
/// e executa e este script, depois de NpcReply.TryParse validar a resposta.
///   neutro -> comportamento padrao (patrulha / segue / conversa)
///   alerta -> para, encara o jogador e fica em guarda por alertSeconds
///   missao -> guia o jogador ate missionPoint (espera se ele ficar para tras)
/// </summary>
public sealed class NpcCapsuleController : MonoBehaviour
{
    private enum State { Patrolling, Chasing, Talking, Idle, Alert, Guiding }

    [Header("Referências")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private NpcDialogue dialogue;
    [Tooltip("Destino da intencao 'missao'. Se vazio, usa o ultimo ponto de patrulha.")]
    [SerializeField] private Transform missionPoint;

    [Header("Movimento (NavMeshAgent)")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Patrulha")]
    [SerializeField] private float waypointTolerance = 0.6f;
    [SerializeField] private float waitTimeAtPoint = 1.5f;

    [Header("Detecção do jogador")]
    [SerializeField] private float chaseRadius = 6f;
    [SerializeField] private float stopChaseRadius = 9f;

    [Header("Conversa")]
    [SerializeField] private float talkRadius = 1.8f;
    [SerializeField] private float conversationCooldown = 15f;

    [Header("Intenções")]
    [SerializeField] private float alertSeconds = 10f;
    [SerializeField] private float guideWaitDistance = 7f;
    [SerializeField] private float missionArriveDistance = 2f;

    /// Intencao atual (ultima valida) e humor, para a UI.
    public NpcIntent Current { get; private set; } = NpcIntent.Neutro;
    public string Mood { get; private set; } = "calmo";
    public event Action<NpcIntent, string> Changed;

    private NavMeshAgent agent;
    private int currentPatrolIndex;
    private float waitTimer;
    private float conversationCooldownTimer;
    private float alertEnds;
    private float nextRepath;
    private bool hasGreetedThisEncounter;
    private State currentState = State.Patrolling;

    private void Awake()
    {
        // CharacterController e NavMeshAgent brigam pelo movimento: o agente manda.
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        agent.angularSpeed = rotationSpeed * 45f;
        agent.acceleration = 20f;

        if (player == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }
        if (dialogue == null) dialogue = GetComponent<NpcDialogue>();
    }

    private void Start()
    {
        currentState = DefaultState();
        Changed?.Invoke(Current, Mood);
    }

    // ------------------------------------------------------------------ intencoes (vindas do LLM, ja validadas)
    public void ApplyIntent(NpcReply reply)
    {
        NpcIntent intent = reply.Intent;
        if (intent == NpcIntent.Missao && MissionDestination() == null)
            intent = NpcIntent.Neutro;                     // sem destino: nao ha o que guiar

        Current = intent;
        Mood = reply.humor;

        switch (intent)
        {
            case NpcIntent.Alerta:
                currentState = State.Alert;
                alertEnds = Time.time + alertSeconds;
                StopAgent();
                break;
            case NpcIntent.Missao:
                currentState = State.Guiding;
                nextRepath = 0f;
                break;
            default:
                currentState = DefaultState();
                break;
        }
        Changed?.Invoke(Current, Mood);
    }

    private void EndIntent(string mood)
    {
        Current = NpcIntent.Neutro;
        Mood = mood;
        currentState = DefaultState();
        Changed?.Invoke(Current, Mood);
    }

    private State DefaultState() => patrolPoints != null && patrolPoints.Length > 0 ? State.Patrolling : State.Idle;

    private Transform MissionDestination()
    {
        if (missionPoint != null) return missionPoint;
        if (patrolPoints != null && patrolPoints.Length > 0) return patrolPoints[patrolPoints.Length - 1];
        return null;
    }

    // ------------------------------------------------------------------ loop
    private void Update()
    {
        if (!agent.isOnNavMesh) return;                    // sem NavMesh: fica parado (veja o README)

        if (conversationCooldownTimer > 0f)
            conversationCooldownTimer -= Time.deltaTime;

        if (currentState == State.Alert) { TickAlert(); return; }
        if (currentState == State.Guiding) { TickGuiding(); return; }

        UpdateState();

        switch (currentState)
        {
            case State.Patrolling: Patrol(); break;
            case State.Chasing: if (player != null) MoveTo(player.position, 1.2f); break;
            case State.Talking:
                StopAgent();
                if (player != null) FaceTarget(player.position);
                break;
            case State.Idle: StopAgent(); break;
        }
    }

    private void TickAlert()
    {
        StopAgent();
        if (player != null) FaceTarget(player.position);
        if (Time.time >= alertEnds) EndIntent("calmo");
    }

    private void TickGuiding()
    {
        Transform dest = MissionDestination();
        if (dest == null || player == null) { EndIntent("calmo"); return; }

        if (FlatDistance(transform.position, dest.position) <= missionArriveDistance)
        {
            StopAgent();
            EndIntent("animado");                          // missao cumprida: volta ao padrao
            return;
        }

        if (FlatDistance(transform.position, player.position) > guideWaitDistance)
        {
            StopAgent();                                   // espera o jogador alcancar
            FaceTarget(player.position);
        }
        else MoveTo(dest.position, 0.5f);
    }

    // ------------------------------------------------------------------ logica base (patrulha / perseguicao / conversa)
    private void UpdateState()
    {
        if (player == null)
        {
            currentState = DefaultState();
            return;
        }

        float distanceToPlayer = FlatDistance(transform.position, player.position);

        if (distanceToPlayer <= talkRadius)
        {
            if (currentState != State.Talking)
            {
                FaceTarget(player.position);
                if (!hasGreetedThisEncounter && conversationCooldownTimer <= 0f && dialogue != null)
                {
                    dialogue.StartConversation();
                    hasGreetedThisEncounter = true;
                    conversationCooldownTimer = conversationCooldown;
                }
            }
            currentState = State.Talking;
            return;
        }

        if (distanceToPlayer > talkRadius + 0.5f)
            hasGreetedThisEncounter = false;

        if (currentState == State.Chasing && distanceToPlayer > stopChaseRadius)
            currentState = DefaultState();
        else if (currentState != State.Chasing && distanceToPlayer <= chaseRadius)
            currentState = State.Chasing;
        else if (currentState == State.Talking)
            currentState = State.Chasing;                  // saiu do raio de conversa: volta a seguir
    }

    private void Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) { StopAgent(); return; }

        Transform target = patrolPoints[currentPatrolIndex];
        if (FlatDistance(transform.position, target.position) <= Mathf.Max(waypointTolerance, 0.6f))
        {
            StopAgent();
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitTimeAtPoint)
            {
                waitTimer = 0f;
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            }
            return;
        }
        MoveTo(target.position, 0.1f);
    }

    // ------------------------------------------------------------------ movimento (somente via NavMeshAgent)
    private void MoveTo(Vector3 destination, float stopDistance)
    {
        agent.stoppingDistance = stopDistance;
        agent.isStopped = false;
        if (Time.time < nextRepath) return;
        nextRepath = Time.time + 0.25f;
        agent.SetDestination(destination);
    }

    private void StopAgent()
    {
        if (!agent.isStopped) agent.isStopped = true;
        nextRepath = 0f;
    }

    private void FaceTarget(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float smoothed = Mathf.LerpAngle(transform.eulerAngles.y, targetAngle, rotationSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, smoothed, 0f);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopChaseRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, talkRadius);
    }
}
