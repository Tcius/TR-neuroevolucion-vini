using System.Collections.Generic;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    private Pacman pacmanScript;
    private GameManager gameManager;
    private EvolutionManager.Genome myGenomePrivate;
    private float timeAlive = 0f;
    private int individualScore = 0;
    private bool isDead = false;
    private bool completedMaze = false;

    private HashSet<Vector2Int> visitedTiles = new HashSet<Vector2Int>();
    private float timeSinceLastProgress = 0f;
    private float timeSinceLastPellet = 0f;
    private int newTilesVisited = 0;
    private bool diedFromStagnation = false;

    private const float MaxTimeWithoutProgress = 15f;
    private const float StagnationPenalty = 100f;
    private const float CompletionReward = 5000f;

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
        gameManager = GetComponentInParent<GameManager>();
        myGenomePrivate = genome;
        timeAlive = 0f;
        individualScore = 0;
        isDead = false;
        completedMaze = false;
        diedFromStagnation = false;

        visitedTiles.Clear();

        Vector2Int startingTile = GetCurrentTile();
        visitedTiles.Add(startingTile);

        timeSinceLastProgress = 0f;
        timeSinceLastPellet = 0f;
        newTilesVisited = 0;
    }

    private void Update()
    {
        if (isDead) return;

        timeAlive += Time.deltaTime;
        timeSinceLastProgress += Time.deltaTime;
        timeSinceLastPellet += Time.deltaTime;

        Vector2Int currentTile = GetCurrentTile();

        if (visitedTiles.Add(currentTile))
        {
            newTilesVisited++;
            timeSinceLastProgress = 0f;
        }

        if (timeSinceLastProgress >= MaxTimeWithoutProgress)
        {
            diedFromStagnation = true;
            UpdateFitness();
            Die();
            return;
        }

        if (pacmanScript != null && pacmanScript.NeedsDecision)
        {
            float[] inputs = GetSensorInputs();
            float[] outputs =
                myGenomePrivate.network.FeedForward(inputs, 4);

            pacmanScript.ProcessOutputs(outputs);
        }

        UpdateFitness();
    }

    private float[] GetSensorInputs()
    {
        float[] inputs = new float[13];

        Vector2[] directions =
        {
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right
        };

        LayerMask obstacleMask =
            LayerMask.GetMask("Obstacle");

        float maxDistance = 10f;

        for (int i = 0; i < 4; i++)
        {
            RaycastHit2D hit =
                Physics2D.Raycast(
                    transform.position,
                    directions[i],
                    maxDistance,
                    obstacleMask
                );

            if (hit.collider != null)
            {
                inputs[i] =
                    hit.distance / maxDistance;
            }
            else
            {
                inputs[i] = 1f;
            }
        }

        inputs[4] = 0f;
        inputs[5] = 0f;
        inputs[6] = 0f;
        inputs[7] = 0f;

        inputs[8] = 0f;
        inputs[9] = 0f;

        GameObject closestPellet =
            FindClosestWithTag("Pellet");

        if (closestPellet != null)
        {
            Vector2 diff =
                (
                    closestPellet.transform.position -
                    transform.position
                ).normalized;

            inputs[8] = diff.x;
            inputs[9] = diff.y;
        }

        inputs[10] = 0f;
        inputs[11] = 0f;

        GameObject closestPowerPellet =
            FindClosestWithTag("PowerPellet");

        if (closestPowerPellet != null)
        {
            Vector2 diffPower =
                (
                    closestPowerPellet.transform.position -
                    transform.position
                ).normalized;

            inputs[10] = diffPower.x;
            inputs[11] = diffPower.y;
        }

        inputs[12] = 0f;

        return inputs;
    }

    private GameObject FindClosestWithTag(string tag)
    {
        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (gameManager == null ||
            gameManager.pellets == null)
        {
            return null;
        }

        GameObject closest = null;
        float minDistance = Mathf.Infinity;

        Vector3 currentPos =
            transform.position;

        foreach (Transform pellet in gameManager.pellets)
        {
            if (pellet == null ||
                !pellet.gameObject.activeInHierarchy ||
                !pellet.CompareTag(tag))
            {
                continue;
            }

            float distance =
                Vector3.Distance(
                    pellet.position,
                    currentPos
                );

            if (distance < minDistance &&
                distance < 15f)
            {
                closest = pellet.gameObject;
                minDistance = distance;
            }
        }

        return closest;
    }

    private void UpdateFitness()
    {
        myGenomePrivate.fitness =
            individualScore
            + (newTilesVisited * 2f)
            + (completedMaze ? CompletionReward : 0f)
            - (diedFromStagnation ? StagnationPenalty : 0f);
    }

    public void CompleteMaze()
    {
        if (isDead) return;

        completedMaze = true;
        diedFromStagnation = false;
        Debug.Log("Maze completed by agent: " + FindAnyObjectByType<EvolutionManager>().generationCount);
        UpdateFitness();
        Die();
    }

    private Vector2Int GetCurrentTile()
    {
        return new Vector2Int(
            Mathf.RoundToInt(transform.position.x),
            Mathf.RoundToInt(transform.position.y)
        );
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead) return;

        if (other.CompareTag("Pellet"))
        {
            individualScore += 10;
            timeSinceLastProgress = 0f;
            timeSinceLastPellet = 0f;
            other.gameObject.SetActive(false);
        }
        else if (other.CompareTag("PowerPellet"))
        {
            individualScore += 50;
            timeSinceLastProgress = 0f;
            timeSinceLastPellet = 0f;
            other.gameObject.SetActive(false);
        }
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

    public float GetTimeSinceLastPellet()
    {
        return timeSinceLastPellet;
    }


    public int GetRemainingPellets()
    {
        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (gameManager == null ||
            gameManager.pellets == null)
        {
            return 0;
        }

        int remaining = 0;

        foreach (Transform pellet in gameManager.pellets)
        {
            if (pellet != null &&
                pellet.gameObject.activeSelf)
            {
                remaining++;
            }
        }

        return remaining;
    }
    public void SetHighlight(bool isLeader)
    {
        SpriteRenderer sprite =
            GetComponent<SpriteRenderer>();

        if (sprite == null)
        {
            sprite =
                GetComponentInChildren<SpriteRenderer>();
        }

        if (sprite != null)
        {
            sprite.color =
                isLeader
                    ? Color.red
                    : Color.yellow;
        }

        transform.localScale =
            Vector3.one;
    }

    public float[] GetSensorInputsForVisualizer()
    {
        return GetSensorInputs();
    }
}