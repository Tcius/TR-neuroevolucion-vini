using System.Collections.Generic;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    private Pacman pacmanScript;
    private EvolutionManager.Genome myGenomePrivate;
    private float timeAlive = 0f;
    private int individualScore = 0;
    private bool isDead = false;

    private HashSet<Vector2Int> visitedTiles = new HashSet<Vector2Int>();
    private float timeSinceLastProgress = 0f;
    private int newTilesVisited = 0;
    private bool diedFromStagnation = false;

    private const float MaxTimeWithoutProgress = 3.5f;
    private const float StagnationPenalty = 50f;

    public EvolutionManager.Genome MyGenome
    {
        get { return myGenomePrivate; }
    }

    public void Setup(EvolutionManager.Genome genome)
    {
        pacmanScript = GetComponent<Pacman>();
        myGenomePrivate = genome;
        timeAlive = 0f;
        individualScore = 0;
        isDead = false;
        diedFromStagnation = false;

        visitedTiles.Clear();

        Vector2Int startingTile = GetCurrentTile();
        visitedTiles.Add(startingTile);

        timeSinceLastProgress = 0f;
        newTilesVisited = 0;
    }

    private void Update()
    {
        if (isDead) return;

        timeAlive += Time.deltaTime;
        timeSinceLastProgress += Time.deltaTime;

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

        float[] inputs = GetSensorInputs();
        float[] outputs = myGenomePrivate.network.FeedForward(inputs, 4);

        pacmanScript.ProcessOutputs(outputs);

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

        LayerMask obstacleMask = LayerMask.GetMask("Obstacle");
        float maxDistance = 10f;

        for (int i = 0; i < 4; i++)
        {
            RaycastHit2D hit = Physics2D.Raycast(
                transform.position,
                directions[i],
                maxDistance,
                obstacleMask
            );

            if (hit.collider != null)
            {
                inputs[i] = hit.distance / maxDistance;
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

        GameObject closestPellet = FindClosestWithTag("Pellet");

        if (closestPellet != null)
        {
            Vector2 diff = (closestPellet.transform.position - transform.position).normalized;
            inputs[8] = diff.x;
            inputs[9] = diff.y;
        }

        inputs[10] = 0f;
        inputs[11] = 0f;

        GameObject closestPowerPellet = FindClosestWithTag("PowerPellet");

        if (closestPowerPellet != null)
        {
            Vector2 diffPower = (closestPowerPellet.transform.position - transform.position).normalized;
            inputs[10] = diffPower.x;
            inputs[11] = diffPower.y;
        }

        inputs[12] = 0f;

        return inputs;
    }

    private GameObject FindClosestWithTag(string tag)
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag(tag);
        GameObject closest = null;
        float minDistance = Mathf.Infinity;
        Vector3 currentPos = transform.position;

        foreach (GameObject target in targets)
        {
            float distance = Vector3.Distance(target.transform.position, currentPos);
            if (distance < minDistance && distance < 15f)
            {
                closest = target;
                minDistance = distance;
            }
        }

        return closest;
    }

    private void UpdateFitness()
    {
        myGenomePrivate.fitness =
            individualScore
            + (newTilesVisited * 1.5f)
            + (timeAlive * 0.1f)
            - (diedFromStagnation ? StagnationPenalty : 0f);
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
            other.gameObject.SetActive(false);
        }
        else if (other.CompareTag("PowerPellet"))
        {
            individualScore += 50;
            timeSinceLastProgress = 0f;
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
        return myGenomePrivate != null ? myGenomePrivate.fitness : 0f;
    }

    public void SetHighlight(bool isLeader)
    {
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();

        if (sprite == null)
            sprite = GetComponentInChildren<SpriteRenderer>();

        if (sprite != null)
        {
            if (isLeader)
                sprite.color = Color.green;
            else
                sprite.color = Color.yellow;
        }

        transform.localScale = Vector3.one;
    }

    public float[] GetSensorInputsForVisualizer()
    {
        return GetSensorInputs();
    }
}