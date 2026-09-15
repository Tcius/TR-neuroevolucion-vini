using System.Collections.Generic;
using UnityEngine;

public class PopulationSpawner : MonoBehaviour
{
    public GameObject mazePrefab;
    public int totalPopulation = 50;
    public int columns = 10;
    public float spacingX = 30f;
    public float spacingY = 30f;

    private List<GameObject> spawnedMazes = new List<GameObject>();
    private List<AgentController> activeAgents = new List<AgentController>();

    public List<AgentController> SpawnPopulation(List<EvolutionManager.Genome> genomes)
    {
        ClearPopulation();

        for (int i = 0; i < genomes.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;

            Vector3 spawnPosition = new Vector3(col * spacingX, -row * spacingY, 0f);

            GameObject mazeInstance = Instantiate(mazePrefab, spawnPosition, Quaternion.identity);
            spawnedMazes.Add(mazeInstance);
            mazeInstance.GetComponent<GameManager>().NewGame();
            AgentController agent = mazeInstance.GetComponentInChildren<AgentController>();
            if (agent != null)
            {
                agent.Setup(genomes[i]);
                activeAgents.Add(agent);
            }
        }

        return activeAgents;
    }

    public void ClearPopulation()
    {
        foreach (GameObject maze in spawnedMazes)
        {
            if (maze != null)
            {
                Destroy(maze);
            }
        }
        spawnedMazes.Clear();
        activeAgents.Clear();
    }
}