using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Triggers chilling paranormal events across the Backrooms where doors rattle violently,
/// slam wide open simultaneously throughout the complex, remain open for a few seconds,
/// and then all slam shut together with an echoing reverberation.
/// </summary>
public class BackroomsDoorEventManager : MonoBehaviour
{
    public static BackroomsDoorEventManager Instance { get; private set; }

    [Header("Event Timing")]
    [Tooltip("Minimum seconds between random door events.")]
    public float minInterval = 45.0f;
    [Tooltip("Maximum seconds between random door events.")]
    public float maxInterval = 90.0f;
    [Tooltip("Initial quiet grace period when level starts.")]
    public float initialGracePeriod = 25.0f;

    [Header("Door Event Parameters")]
    [Tooltip("Seconds all doors rattle before bursting open.")]
    public float rattleDuration = 1.8f;
    [Tooltip("Seconds doors stay wide open before slamming shut.")]
    public float doorsStayOpenDuration = 4.0f;

    [Header("Psychological Variation")]
    [Tooltip("Chance that an event is a single eerie distant door slam instead of map-wide mayhem.")]
    [Range(0f, 1f)] public float singleDoorChance = 0.35f;

    public bool IsEventActive { get; private set; }

    private Coroutine eventLoopRoutine;
    private AudioSource ambientAudioSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ambientAudioSource = gameObject.AddComponent<AudioSource>();
        ambientAudioSource.playOnAwake = false;
        ambientAudioSource.spatialBlend = 0f; // 2D ambient shudder
    }

    private void Start()
    {
        eventLoopRoutine = StartCoroutine(DoorEventLoop());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (eventLoopRoutine != null) StopCoroutine(eventLoopRoutine);
    }

    private IEnumerator DoorEventLoop()
    {
        yield return new WaitForSeconds(initialGracePeriod);

        while (true)
        {
            float waitTime = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(waitTime);

            if (!GameOverController.IsGameOver && !PauseMenuController.IsPaused && !VictoryController.IsGameWon)
            {
                if (Random.value < singleDoorChance && BackroomsDoor.AllDoors.Count > 1)
                {
                    yield return StartCoroutine(ExecuteSingleDistantDoorEvent());
                }
                else
                {
                    yield return StartCoroutine(ExecuteAllDoorsEventRoutine());
                }
            }
        }
    }

    [ContextMenu("Trigger All Doors Event Now")]
    public void TriggerImmediateEvent()
    {
        if (IsEventActive) return;
        StartCoroutine(ExecuteAllDoorsEventRoutine());
    }

    /// <summary>
    /// Massive map-wide paranormal event: all doors rattle, slam open, wait, then slam shut!
    /// </summary>
    public IEnumerator ExecuteAllDoorsEventRoutine()
    {
        if (BackroomsDoor.AllDoors.Count == 0) yield break;

        IsEventActive = true;

        var doors = BackroomsDoor.AllDoors;

        // 1. WARNING PHASE: All doors across the Backrooms rattle furiously
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null)
            {
                doors[i].StartRattle(rattleDuration);
            }
        }

        yield return new WaitForSeconds(rattleDuration);

        // 2. SLAM OPEN: All doors burst wide open together!
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null)
            {
                doors[i].SlamOpen();
            }
        }

        // Camera micro-shake or glitch if player is near any door
        Transform player = Camera.main != null ? Camera.main.transform : null;
        if (player != null)
        {
            PlayerNoiseEmitter.EmitNoise(player.position, 16f);
        }

        // 3. SUSPENSION: Doors hang wide open in the dead silence
        yield return new WaitForSeconds(doorsStayOpenDuration);

        // 4. SLAM SHUT: All doors violently slam shut in unison!
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null)
            {
                doors[i].SlamShut();
            }
        }

        // Alert villain to the massive reverberation
        if (player != null)
        {
            PlayerNoiseEmitter.EmitNoise(player.position, 20f);
        }

        yield return new WaitForSeconds(1.0f);
        IsEventActive = false;
    }

    /// <summary>
    /// Eerie single distant door opening or slamming in the darkness to keep the player on edge.
    /// </summary>
    private IEnumerator ExecuteSingleDistantDoorEvent()
    {
        var doors = BackroomsDoor.AllDoors;
        if (doors.Count == 0) yield break;

        // Pick a random door
        BackroomsDoor chosenDoor = doors[Random.Range(0, doors.Count)];
        if (chosenDoor == null) yield break;

        if (chosenDoor.IsOpen)
        {
            chosenDoor.SlamShut();
        }
        else
        {
            chosenDoor.SlamOpen();
            yield return new WaitForSeconds(Random.Range(3.0f, 6.0f));
            if (chosenDoor != null && chosenDoor.IsOpen)
            {
                chosenDoor.SlamShut();
            }
        }
    }
}
