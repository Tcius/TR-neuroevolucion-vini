using System.Collections.Generic;

using UnityEngine;

public class AgentController : MonoBehaviour

{

    private Pacman pacmanScript;

    private PacmanSensors pacmanSensors;

    private GameManager gameManager;

    private EvolutionManager.Genome myGenomePrivate;

    private float timeAlive = 0f;

    private int individualScore = 0;

    private bool isDead = false;

    private bool completedMaze = false;

    private HashSet<Vector2Int> visitedTiles = new HashSet<Vector2Int>();

    private float timeSinceLastProgress = 0f;

    private int newTilesVisited = 0;

    private bool diedFromStagnation = false;

    private float ghostDeathPenalty = 0f;

    private int minimumPelletsRemaining = int.MaxValue;

    private float completionProgressReward = 0f;

    private const float MaxTimeWithoutProgress = 15f;

    private const float MaxTimeWithoutProgressNearEnd = 25f;

    private const float MaxTimeWithoutProgressFinal = 40f;

    private const int NearEndPelletThreshold = 10;

    private const int FinalPelletThreshold = 4;

    private const float MaxStagnationPenalty = 500f;

    private const float StagnationPenaltyZeroTime = 30f;

    private const float CompletionReward = 5000f;

    private const float CompletionTimePenaltyPerSecond = 0f;

    private const float GhostDeathMaxPenalty = 2500f;
    private const float GhostDeathMinPenalty = 250f;
    private const float GhostDeathPenaltyReferenceTime = 100f;

    private const float CompletionProgressRewardMax = 4000f;

    private const float CompletionProgressRewardScale = 12f;

    public EvolutionManager.Genome MyGenome

    {

        get { return myGenomePrivate; }

    }

    public bool HasCompletedMaze

    {

        get { return completedMaze; }

    }

    public void Setup(EvolutionManager.Genome genome)

    {

        pacmanScript = GetComponent<Pacman>();

        pacmanSensors = GetComponent<PacmanSensors>();

        if (pacmanSensors == null)

        {

            pacmanSensors = gameObject.AddComponent<PacmanSensors>();

        }

        gameManager = GetComponentInParent<GameManager>();

        myGenomePrivate = genome;

        if (pacmanScript != null)
        {
            pacmanScript.allowStrategicReverse =
                myGenomePrivate != null &&
                myGenomePrivate.network != null &&
                myGenomePrivate.network.manualNavigation;
        }

        if (myGenomePrivate != null)

        {

            myGenomePrivate.fitness = 0f;

        }

        timeAlive = 0f;

        individualScore = 0;

        isDead = false;

        completedMaze = false;

        diedFromStagnation = false;

        ghostDeathPenalty = 0f;

        visitedTiles.Clear();

        Vector2Int startingTile = GetCurrentTile();

        visitedTiles.Add(startingTile);

        timeSinceLastProgress = 0f;

        newTilesVisited = 0;

        minimumPelletsRemaining = int.MaxValue;

        completionProgressReward = 0f;

        UpdateMinimumPelletsRemaining();

        UpdateCompletionProgressReward();

    }

    private void Update()

    {

        if (isDead || myGenomePrivate == null)

        {

            return;

        }

        timeAlive += Time.deltaTime;

        timeSinceLastProgress += Time.deltaTime;

        UpdateMinimumPelletsRemaining();

        UpdateCompletionProgressReward();

        Vector2Int currentTile = GetCurrentTile();

        if (visitedTiles.Add(currentTile))

        {

            newTilesVisited++;

            timeSinceLastProgress = 0f;

        }

        float currentMaxTimeWithoutProgress = GetCurrentMaxTimeWithoutProgress();

        if (timeSinceLastProgress >= currentMaxTimeWithoutProgress)

        {

            diedFromStagnation = true;

            UpdateFitness();

            Die();

            return;

        }

        if (pacmanScript != null && pacmanScript.NeedsDecision)

        {

            float[] inputs = GetSensorInputs();

            float[] outputs = myGenomePrivate.network.FeedForward(inputs, 4);

            pacmanScript.ProcessOutputs(outputs);

        }

        UpdateFitness();

    }

    private float[] GetSensorInputs()

    {

        if (pacmanSensors == null)

        {

            pacmanSensors = GetComponent<PacmanSensors>();

            if (pacmanSensors == null)

            {

                pacmanSensors = gameObject.AddComponent<PacmanSensors>();

            }

        }

        if (myGenomePrivate != null &&
            myGenomePrivate.network != null &&
            myGenomePrivate.network.manualNavigation)
        {
            return pacmanSensors.GetManualInputs();
        }

        GameObject closestPellet = FindClosestWithTag("Pellet");

        GameObject closestPowerPellet = FindClosestWithTag("PowerPellet");

        return pacmanSensors.GetInputs(

            closestPellet != null ? closestPellet.transform : null,

            closestPowerPellet != null ? closestPowerPellet.transform : null

        );

    }

    private GameObject FindClosestWithTag(string tag)

    {

        if (gameManager == null)

        {

            gameManager = GetComponentInParent<GameManager>();

        }

        if (gameManager == null || gameManager.pellets == null)

        {

            return null;

        }

        GameObject closest = null;

        float minDistance = Mathf.Infinity;

        Vector3 currentPos = transform.position;

        foreach (Transform pellet in gameManager.pellets)

        {

            if (pellet == null ||

                !pellet.gameObject.activeInHierarchy ||

                !pellet.CompareTag(tag))

            {

                continue;

            }

            float distance = Vector3.Distance(

                pellet.position,

                currentPos

            );

            if (distance < minDistance && distance < 15f)

            {

                closest = pellet.gameObject;

                minDistance = distance;

            }

        }

        return closest;

    }

    private void UpdateFitness()

    {

        if (myGenomePrivate == null)

        {

            return;

        }

        float stagnationPenalty = 0f;

        if (diedFromStagnation)

        {

            float survivalFactor = Mathf.Clamp01(

                timeAlive / StagnationPenaltyZeroTime

            );

            stagnationPenalty = MaxStagnationPenalty * (

                1f - survivalFactor

            );

        }

        float completionTimePenalty = completedMaze

            ? timeAlive * CompletionTimePenaltyPerSecond

            : 0f;

        myGenomePrivate.fitness =

            individualScore

            + (newTilesVisited * 2f)

            + (completedMaze ? CompletionReward : 0f)

            - stagnationPenalty

            + completionProgressReward

            - ghostDeathPenalty

            - completionTimePenalty;

    }

    public void CompleteMaze()

    {

        if (isDead)

        {

            return;

        }

        completedMaze = true;

        diedFromStagnation = false;

        ghostDeathPenalty = 0f;

        EvolutionManager evolutionManager =

            FindAnyObjectByType<EvolutionManager>();

        if (evolutionManager != null)

        {

            Debug.Log(

                "Maze completed by agent: " +

                evolutionManager.generationCount

            );

        }

        UpdateFitness();

        Die();

    }

    public void RegisterPelletEaten(int points)

    {

        if (isDead)

        {

            return;

        }

        individualScore += points;

        timeSinceLastProgress = 0f;

        UpdateMinimumPelletsRemaining();

        UpdateFitness();

    }

    public void RegisterGhostEaten(int points)

    {

        if (isDead)

        {

            return;

        }

        individualScore += points;

        timeSinceLastProgress = 0f;

        UpdateMinimumPelletsRemaining();

        UpdateFitness();

    }

    public void RegisterKilledByGhost()

    {

        if (isDead || completedMaze)

        {

            return;

        }

        diedFromStagnation = false;

        float survivalFactor = Mathf.Clamp01(
            timeAlive / GhostDeathPenaltyReferenceTime
        );

        ghostDeathPenalty = Mathf.Lerp(
            GhostDeathMaxPenalty,
            GhostDeathMinPenalty,
            survivalFactor
        );

        UpdateFitness();

        Die();

    }

    private float GetCurrentMaxTimeWithoutProgress()

    {

        int remainingPellets = GetRemainingPellets();

        if (remainingPellets >= 0 &&

            remainingPellets <= FinalPelletThreshold)

        {

            return MaxTimeWithoutProgressFinal;

        }

        if (remainingPellets >= 0 &&

            remainingPellets <= NearEndPelletThreshold)

        {

            return MaxTimeWithoutProgressNearEnd;

        }

        return MaxTimeWithoutProgress;

    }

    private Vector2Int GetCurrentTile()

    {

        return new Vector2Int(

            Mathf.RoundToInt(transform.position.x),

            Mathf.RoundToInt(transform.position.y)

        );

    }

    public void Die()

    {

        if (!isDead)

        {

            isDead = true;

            gameObject.SetActive(false);

        }

    }

    public float GetFitness()

    {

        return myGenomePrivate != null

            ? myGenomePrivate.fitness

            : 0f;

    }

    public float GetCompletionTime()

    {

        return completedMaze

            ? timeAlive

            : -1f;

    }

    private void UpdateMinimumPelletsRemaining()

    {

        int remainingPellets = GetRemainingPellets();

        if (remainingPellets >= 0 &&

            remainingPellets < minimumPelletsRemaining)

        {

            minimumPelletsRemaining = remainingPellets;

        }

    }

    private void UpdateCompletionProgressReward()

    {

        if (minimumPelletsRemaining < 0 ||

            minimumPelletsRemaining == int.MaxValue)

        {

            completionProgressReward = 0f;

            return;

        }

        completionProgressReward =

            CompletionProgressRewardMax *

            Mathf.Exp(

                -minimumPelletsRemaining /

                CompletionProgressRewardScale

            );

    }

    public int GetMinimumPelletsRemaining()

    {

        return minimumPelletsRemaining == int.MaxValue

            ? -1

            : minimumPelletsRemaining;

    }

    public int GetRemainingPellets()

    {

        if (gameManager == null)

        {

            gameManager = GetComponentInParent<GameManager>();

        }

        if (gameManager == null || gameManager.pellets == null)

        {

            return -1;

        }

        int remaining = 0;

        foreach (Transform pellet in gameManager.pellets)

        {

            if (pellet != null && pellet.gameObject.activeSelf)

            {

                remaining++;

            }

        }

        return remaining;

    }

    public void SetHighlight(bool isLeader)

    {

        SpriteRenderer sprite = GetComponent<SpriteRenderer>();

        if (sprite == null)

        {

            sprite = GetComponentInChildren<SpriteRenderer>();

        }

        if (sprite != null)

        {

            sprite.color = isLeader ? Color.red : Color.yellow;

        }

        transform.localScale = Vector3.one;

    }

    public float[] GetSensorInputsForVisualizer()

    {

        return GetSensorInputs();

    }

}
