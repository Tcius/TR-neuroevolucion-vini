using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EvolutionManager : MonoBehaviour
{
    [Header("Evolution Settings")]
    public int populationSize = 45;
    public float generationDuration = 20f;

    [Header("Manual Network")]
    public bool useManualNetworkPopulation = false;
    public TextAsset manualNetworkJson;

    [Header("NEAT Settings")]
    [Range(0f, 1f)] public float crossoverRate = 0.75f;
    [Range(0f, 1f)] public float weightMutationRate = 0.15f;
    [Range(0f, 1f)] public float addConnectionMutationRate = 0.08f;
    [Range(0f, 1f)] public float addNodeMutationRate = 0.05f;
    [Range(0f, 1f)] public float reenableConnectionMutationRate = 0.02f;
    [Range(0f, 1f)] public float weightResetRate = 0.10f;

    public float weightPerturbation = 0.25f;

    [Header("Speciation")]
    public float compatibilityThreshold = 3f;
    public float excessCoefficient = 1f;
    public float disjointCoefficient = 1f;
    public float weightDifferenceCoefficient = 0.4f;
    public int maxEliteSpecies = 2;
    public int tournamentSize = 3;
    public int topGenomesToSave = 10;

    [HideInInspector] public List<Genome> population =
        new List<Genome>();

    [HideInInspector] public List<AgentController> activeAgents =
        new List<AgentController>();

    [HideInInspector] public int generationCount = 1;
    [HideInInspector] public float timer = 0f;

    private const string SaveFolderName =
        "genomes";

    private const int ExistingInputCount = 13;
    private const int DirectionInputNodeId = 1000000;
    private const int OutputStartNodeId = 13;
    private const int OutputCount = 4;

    private string HistoricalBestFileName;
    private string HistoricalBestGenomeFileName;
    private string BestCompletedGenomeFileName;
    private string BestProgressGenomeFileName;
    private string BestAlternativeProgressGenomeFileName;

    private const string GenerationHistoryFileName =
        "generation_history.json";

    [Header("Difficulty Stage")]
    public int ghostCountStageOverride = 4;

    private int stageGhostCount = -1;

    private const int ProtectedCompletedCopies = 5;
    private const int ProtectedProgressCopies = 5;

    private PopulationSpawner spawner;
    private string saveFolderPath;

    private readonly Dictionary<string, int>
        innovationHistory =
        new Dictionary<string, int>();

    private int nextInnovationNumber = 1;
    private int lastSpeciesCount = 0;

    private float historicalBestFitness =
        float.MinValue;

    private Genome historicalBestGenome;
    private Genome bestCompletedGenome;
    private Genome bestProgressGenome;
    private int bestProgressMinimumPelletsRemaining = int.MaxValue;
    private Genome bestAlternativeProgressGenome;
    private int bestAlternativeProgressMinimumPelletsRemaining = int.MaxValue;
    private List<GenerationResult> generationHistory =
        new List<GenerationResult>();

    private class Species
    {
        public int id;
        public Genome representative;
        public List<Genome> members =
            new List<Genome>();

        public float totalAdjustedFitness;
    }

    [System.Serializable]
    private class GenerationSave
    {
        public int generation;
        public int ghostCount;
        public float maxFitness;
        public bool completed;
        public int completionCount;
        public float bestCompletionTime;
        public int minimumPelletsRemaining;
        public List<Genome> genomes =
            new List<Genome>();
    }

    [System.Serializable]
    private class HistoricalBestSave
    {
        public float bestFitness;
    }

    [System.Serializable]
    private class BestProgressSave
    {
        public int minimumPelletsRemaining;
        public Genome genome;
    }

    [System.Serializable]
    private class GenerationResult
    {
        public int generation;
        public int ghostCount;
        public float maxFitness;
        public bool completed;
        public int completionCount;
        public float bestCompletionTime;
        public int minimumPelletsRemaining;
    }

    [System.Serializable]
    private class GenerationHistorySave
    {
        public List<GenerationResult> generations =
            new List<GenerationResult>();
    }

    [System.Serializable]
    public class NEATNode
    {
        public enum NodeType
        {
            Input,
            Hidden,
            Output
        }

        public int id;
        public NodeType type;
        public float value;
        public float layer;

        public NEATNode(
            int id,
            NodeType type,
            float layer = 0f)
        {
            this.id = id;
            this.type = type;
            this.value = 0f;
            this.layer = layer;
        }
    }

    [System.Serializable]
    public class NEATConnection
    {
        public NEATNode fromNode;
        public NEATNode toNode;
        public float weight;
        public bool enabled;
        public int innovation;

        public NEATConnection(
            NEATNode fromNode,
            NEATNode toNode,
            float weight,
            int innovation = 0)
        {
            this.fromNode = fromNode;
            this.toNode = toNode;
            this.weight = weight;
            this.enabled = true;
            this.innovation = innovation;
        }
    }

    [System.Serializable]
    public class NeuralNetwork
    {
        public bool manualNavigation;

        public List<NEATNode> nodes =
            new List<NEATNode>();

        public List<NEATConnection> connections =
            new List<NEATConnection>();

        public float[] FeedForward(
            float[] inputValues,
            int outputCount)
        {
            if (nodes == null ||
                nodes.Count == 0)
            {
                return new float[outputCount];
            }

            List<NEATNode> inputNodes =
                nodes.FindAll(
                    n => n.type ==
                    NEATNode.NodeType.Input
                );

            List<NEATNode> outputNodes =
                nodes.FindAll(
                    n => n.type ==
                    NEATNode.NodeType.Output
                );

            List<NEATNode> calculationNodes =
                nodes.FindAll(
                    n => n.type !=
                    NEATNode.NodeType.Input
                );

            inputNodes.Sort(
                (a, b) =>
                    a.id.CompareTo(b.id)
            );

            outputNodes.Sort(
                (a, b) =>
                    a.id.CompareTo(b.id)
            );

            calculationNodes.Sort(
                (a, b) =>
                {
                    int layerCompare =
                        a.layer.CompareTo(b.layer);

                    if (layerCompare != 0)
                    {
                        return layerCompare;
                    }

                    return a.id.CompareTo(b.id);
                }
            );

            for (int i = 0;
                 i < nodes.Count;
                 i++)
            {
                if (nodes[i].type !=
                    NEATNode.NodeType.Input)
                {
                    nodes[i].value = 0f;
                }
            }

            int inputsToAssign =
                Mathf.Min(
                    inputValues.Length,
                    inputNodes.Count
                );

            for (int i = 0;
                 i < inputsToAssign;
                 i++)
            {
                inputNodes[i].value =
                    inputValues[i];
            }

            for (int i = 0;
                 i < calculationNodes.Count;
                 i++)
            {
                NEATNode node =
                    calculationNodes[i];

                float sum = 0f;

                for (int j = 0;
                     j < connections.Count;
                     j++)
                {
                    NEATConnection connection =
                        connections[j];

                    if (connection == null ||
                        !connection.enabled ||
                        connection.fromNode == null ||
                        connection.toNode == null)
                    {
                        continue;
                    }

                    if (connection.toNode.id ==
                        node.id)
                    {
                        sum +=
                            connection.fromNode.value *
                            connection.weight;
                    }
                }

                node.value =
                    (float)System.Math.Tanh(sum);
            }

            float[] outputs =
                new float[outputCount];

            for (int i = 0;
                 i < outputNodes.Count &&
                 i < outputCount;
                 i++)
            {
                outputs[i] =
                    outputNodes[i].value;
            }

            return outputs;
        }
    }

    [System.Serializable]
    public class Genome
    {
        public NeuralNetwork network;
        public float fitness;

        public Genome(NeuralNetwork net)
        {
            network = net;
            fitness = 0f;
        }
    }

    private void Awake()
    {
        saveFolderPath =
            Path.Combine(
                Application.persistentDataPath,
                SaveFolderName
            );
    }

    private void Start()
    {
        spawner =
            FindAnyObjectByType<PopulationSpawner>();

        stageGhostCount =
            ghostCountStageOverride >= 0
                ? ghostCountStageOverride
                : DetectConfiguredGhostCount();

        ConfigureStageFileNames();

        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        

        if (!useManualNetworkPopulation)
        {
            LoadHistoricalBestFitness();
            LoadHistoricalBestGenome();
            LoadBestCompletedGenome();
            LoadBestProgressGenome();
            LoadBestAlternativeProgressGenome();
            LoadGenerationHistory();
        }

        InitializePopulation();
        StartGeneration();
    }

    private void ConfigureStageFileNames()
    {
        string suffix = "_ghosts_" + Mathf.Max(0, stageGhostCount);

        HistoricalBestFileName = "historical_best" + suffix + ".json";
        HistoricalBestGenomeFileName = "historical_best_genome" + suffix + ".json";
        BestCompletedGenomeFileName = "best_completed_genome" + suffix + ".json";
        BestProgressGenomeFileName = "best_progress_genome" + suffix + ".json";
        BestAlternativeProgressGenomeFileName = "best_progress_alternative_genome" + suffix + ".json";
    }

    private int DetectConfiguredGhostCount()
    {
        if (spawner == null || spawner.mazePrefab == null)
        {
            Debug.LogWarning(
                "EvolutionManager: no se ha podido detectar el prefab del laberinto. Configura ghostCountStageOverride con el número de fantasmas de esta fase."
            );
            return 0;
        }

        GameManager prefabGameManager =
            spawner.mazePrefab.GetComponent<GameManager>();

        if (prefabGameManager == null)
        {
            prefabGameManager =
                spawner.mazePrefab.GetComponentInChildren<GameManager>(true);
        }

        if (prefabGameManager == null || prefabGameManager.ghosts == null)
        {
            Debug.LogWarning(
                "EvolutionManager: no se ha encontrado el array de fantasmas en el prefab. Configura ghostCountStageOverride manualmente."
            );
            return 0;
        }

        int configuredReferences = 0;
        int activeConfiguredGhosts = 0;

        for (int i = 0; i < prefabGameManager.ghosts.Length; i++)
        {
            Ghost ghost = prefabGameManager.ghosts[i];

            if (ghost == null)
            {
                continue;
            }

            configuredReferences++;

            if (IsActiveInPrefabHierarchy(
                    ghost.transform,
                    spawner.mazePrefab.transform))
            {
                activeConfiguredGhosts++;
            }
        }

        if (activeConfiguredGhosts > 0)
        {
            return activeConfiguredGhosts;
        }

        return configuredReferences;
    }

    private bool IsActiveInPrefabHierarchy(
        Transform target,
        Transform prefabRoot)
    {
        Transform current = target;

        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                return false;
            }

            if (current == prefabRoot)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (!useManualNetworkPopulation)
        {
            UpdateHistoricalBestFitness();
            UpdateBestCompletedGenome();
            UpdateBestProgressGenome();
        }

        UpdateLeaderHighlight();

        if (AllAgentsDead())
        {
            NextGeneration();
        }
    }

    public float GetCurrentGenerationMaxFitness()
    {
        if (population == null ||
            population.Count == 0)
        {
            return 0f;
        }

        float max =
            float.MinValue;

        for (int i = 0;
             i < population.Count;
             i++)
        {
            if (population[i].fitness > max)
            {
                max =
                    population[i].fitness;
            }
        }

        return max;
    }

    public float GetHistoricalBestFitness()
    {
        if (historicalBestFitness ==
            float.MinValue)
        {
            return 0f;
        }

        return historicalBestFitness;
    }

    public int GetCurrentCompletedCount()
    {
        if (activeAgents == null)
        {
            return 0;
        }

        int completed = 0;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null &&
                activeAgents[i].HasCompletedMaze)
            {
                completed++;
            }
        }

        return completed;
    }

    public float GetCurrentBestCompletionTime()
    {
        if (activeAgents == null)
        {
            return -1f;
        }

        float bestTime =
            float.MaxValue;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            AgentController agent =
                activeAgents[i];

            if (agent == null ||
                !agent.HasCompletedMaze)
            {
                continue;
            }

            float completionTime =
                agent.GetCompletionTime();

            if (completionTime >= 0f &&
                completionTime < bestTime)
            {
                bestTime =
                    completionTime;
            }
        }

        return bestTime == float.MaxValue
            ? -1f
            : bestTime;
    }

    public int GetCurrentMinimumPelletsRemaining()
    {
        if (activeAgents == null || activeAgents.Count == 0)
        {
            return -1;
        }

        int minimumPelletsRemaining = int.MaxValue;
        bool foundAgent = false;

        for (int i = 0; i < activeAgents.Count; i++)
        {
            AgentController agent = activeAgents[i];

            if (agent == null)
            {
                continue;
            }

            int remainingPellets = agent.GetMinimumPelletsRemaining();

            if (remainingPellets < 0)
            {
                continue;
            }

            if (remainingPellets < minimumPelletsRemaining)
            {
                minimumPelletsRemaining = remainingPellets;
            }

            foundAgent = true;
        }

        return foundAgent
            ? minimumPelletsRemaining
            : -1;
    }

    public int GetCurrentSpeciesCount()
    {
        return lastSpeciesCount;
    }

    private void UpdateHistoricalBestFitness()
    {
        if (population == null ||
            population.Count == 0)
        {
            return;
        }

        Genome currentBest =
            GetBestGenome();

        if (currentBest == null)
        {
            return;
        }

        if (historicalBestGenome == null ||
            currentBest.fitness >
            historicalBestFitness)
        {
            historicalBestFitness =
                currentBest.fitness;

            historicalBestGenome =
                CloneGenome(
                    currentBest
                );

            historicalBestGenome.fitness =
                currentBest.fitness;

            SaveHistoricalBestFitness();
            SaveHistoricalBestGenome();
        }
    }

    private void UpdateBestCompletedGenome()
    {
        if (activeAgents == null)
        {
            return;
        }

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            AgentController agent =
                activeAgents[i];

            if (agent == null ||
                !agent.HasCompletedMaze ||
                agent.MyGenome == null)
            {
                continue;
            }

            if (bestCompletedGenome == null ||
                agent.GetFitness() >
                bestCompletedGenome.fitness)
            {
                bestCompletedGenome =
                    CloneGenome(
                        agent.MyGenome
                    );

                bestCompletedGenome.fitness =
                    agent.GetFitness();

                SaveBestCompletedGenome();
                RefreshBestAlternativeProgressGenome();
            }
        }
    }

    private void UpdateBestProgressGenome()
    {
        if (activeAgents == null)
        {
            return;
        }

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            AgentController agent =
                activeAgents[i];

            if (agent == null ||
                agent.MyGenome == null)
            {
                continue;
            }

            int remainingPellets =
                agent.GetMinimumPelletsRemaining();

            if (remainingPellets < 0)
            {
                continue;
            }

            bool isBetter =
                bestProgressGenome == null ||
                remainingPellets <
                bestProgressMinimumPelletsRemaining;

            bool isSameProgressButBetterFitness =
                bestProgressGenome != null &&
                remainingPellets ==
                bestProgressMinimumPelletsRemaining &&
                agent.GetFitness() >
                bestProgressGenome.fitness;

            if (isBetter || isSameProgressButBetterFitness)
            {
                bestProgressGenome =
                    CloneGenome(
                        agent.MyGenome
                    );

                bestProgressGenome.fitness =
                    agent.GetFitness();

                bestProgressMinimumPelletsRemaining =
                    remainingPellets;

                SaveBestProgressGenome();
            }

            if (bestCompletedGenome != null &&
                AreGenomesStructurallyEqual(
                    agent.MyGenome,
                    bestCompletedGenome
                ))
            {
                continue;
            }

            bool isBetterAlternative =
                bestAlternativeProgressGenome == null ||
                remainingPellets <
                bestAlternativeProgressMinimumPelletsRemaining;

            bool isSameAlternativeProgressButBetterFitness =
                bestAlternativeProgressGenome != null &&
                remainingPellets ==
                bestAlternativeProgressMinimumPelletsRemaining &&
                agent.GetFitness() >
                bestAlternativeProgressGenome.fitness;

            if (isBetterAlternative ||
                isSameAlternativeProgressButBetterFitness)
            {
                bestAlternativeProgressGenome =
                    CloneGenome(
                        agent.MyGenome
                    );

                bestAlternativeProgressGenome.fitness =
                    agent.GetFitness();

                bestAlternativeProgressMinimumPelletsRemaining =
                    remainingPellets;

                SaveBestAlternativeProgressGenome();
            }
        }
    }

    private void RefreshBestAlternativeProgressGenome()
    {
        bestAlternativeProgressGenome = null;
        bestAlternativeProgressMinimumPelletsRemaining =
            int.MaxValue;

        if (activeAgents == null)
        {
            return;
        }

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            AgentController agent =
                activeAgents[i];

            if (agent == null ||
                agent.MyGenome == null)
            {
                continue;
            }

            if (bestCompletedGenome != null &&
                AreGenomesStructurallyEqual(
                    agent.MyGenome,
                    bestCompletedGenome
                ))
            {
                continue;
            }

            int remainingPellets =
                agent.GetMinimumPelletsRemaining();

            if (remainingPellets < 0)
            {
                continue;
            }

            bool isBetter =
                bestAlternativeProgressGenome == null ||
                remainingPellets <
                bestAlternativeProgressMinimumPelletsRemaining;

            bool isSameProgressButBetterFitness =
                bestAlternativeProgressGenome != null &&
                remainingPellets ==
                bestAlternativeProgressMinimumPelletsRemaining &&
                agent.GetFitness() >
                bestAlternativeProgressGenome.fitness;

            if (isBetter || isSameProgressButBetterFitness)
            {
                bestAlternativeProgressGenome =
                    CloneGenome(
                        agent.MyGenome
                    );

                bestAlternativeProgressGenome.fitness =
                    agent.GetFitness();

                bestAlternativeProgressMinimumPelletsRemaining =
                    remainingPellets;
            }
        }

        SaveBestAlternativeProgressGenome();
    }

    private void LoadHistoricalBestFitness()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                HistoricalBestFileName
            );

        if (File.Exists(filePath))
        {
            string json =
                File.ReadAllText(filePath);

            HistoricalBestSave save =
                JsonUtility.FromJson<HistoricalBestSave>(
                    json
                );

            if (save != null)
            {
                historicalBestFitness =
                    save.bestFitness;
            }
        }
    }

    private void LoadHistoricalBestGenome()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                HistoricalBestGenomeFileName
            );

        if (File.Exists(filePath))
        {
            string json =
                File.ReadAllText(filePath);

            Genome loadedGenome =
                JsonUtility.FromJson<Genome>(
                    json
                );

            if (loadedGenome != null &&
                loadedGenome.network != null)
            {
                SanitizeNetwork(
                    loadedGenome.network
                );

                historicalBestGenome =
                    loadedGenome;

                if (historicalBestGenome.fitness >
                    historicalBestFitness)
                {
                    historicalBestFitness =
                        historicalBestGenome.fitness;
                }

                return;
            }
        }

        Genome bestFromGenerations =
            FindHistoricalBestGenomeFromGenerations();

        if (bestFromGenerations != null)
        {
            historicalBestGenome =
                bestFromGenerations;

            if (historicalBestGenome.fitness >
                historicalBestFitness)
            {
                historicalBestFitness =
                    historicalBestGenome.fitness;
            }

            SaveHistoricalBestGenome();
        }
    }

    private Genome FindHistoricalBestGenomeFromGenerations()
    {
        if (!Directory.Exists(
                saveFolderPath))
        {
            return null;
        }

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*_top10.json"
            );

        Genome bestGenome =
            null;

        for (int i = 0;
             i < files.Length;
             i++)
        {
            string json =
                File.ReadAllText(
                    files[i]
                );

            GenerationSave save =
                JsonUtility.FromJson<GenerationSave>(
                    json
                );

            if (save == null ||
                save.genomes == null ||
                !json.Contains("\"ghostCount\"") ||
                save.ghostCount != stageGhostCount)
            {
                continue;
            }

            for (int j = 0;
                 j < save.genomes.Count;
                 j++)
            {
                Genome candidate =
                    save.genomes[j];

                if (candidate == null ||
                    candidate.network == null)
                {
                    continue;
                }

                if (bestGenome == null ||
                    candidate.fitness >
                    bestGenome.fitness)
                {
                    bestGenome =
                        CloneGenome(
                            candidate
                        );
                }
            }
        }

        return bestGenome;
    }

    private void LoadBestCompletedGenome()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                BestCompletedGenomeFileName
            );

        if (!File.Exists(filePath))
        {
            return;
        }

        string json =
            File.ReadAllText(filePath);

        Genome loadedGenome =
            JsonUtility.FromJson<Genome>(
                json
            );

        if (loadedGenome != null &&
            loadedGenome.network != null)
        {
            SanitizeNetwork(
                loadedGenome.network
            );

            bestCompletedGenome =
                loadedGenome;
        }
    }

    private void LoadBestProgressGenome()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                BestProgressGenomeFileName
            );

        if (!File.Exists(filePath))
        {
            return;
        }

        string json =
            File.ReadAllText(filePath);

        BestProgressSave save =
            JsonUtility.FromJson<BestProgressSave>(
                json
            );

        if (save == null ||
            save.genome == null ||
            save.genome.network == null ||
            save.minimumPelletsRemaining < 0)
        {
            return;
        }

        SanitizeNetwork(
            save.genome.network
        );

        bestProgressGenome =
            save.genome;

        bestProgressMinimumPelletsRemaining =
            save.minimumPelletsRemaining;
    }

    private void SaveHistoricalBestGenome()
    {
        if (historicalBestGenome == null ||
            historicalBestGenome.network == null)
        {
            return;
        }

        string json =
            JsonUtility.ToJson(
                historicalBestGenome,
                true
            );

        File.WriteAllText(
            Path.Combine(
                saveFolderPath,
                HistoricalBestGenomeFileName
            ),
            json
        );
    }

    private void SaveBestCompletedGenome()
    {
        if (bestCompletedGenome == null ||
            bestCompletedGenome.network == null)
        {
            return;
        }

        string json =
            JsonUtility.ToJson(
                bestCompletedGenome,
                true
            );

        File.WriteAllText(
            Path.Combine(
                saveFolderPath,
                BestCompletedGenomeFileName
            ),
            json
        );
    }

    private void LoadBestAlternativeProgressGenome()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                BestAlternativeProgressGenomeFileName
            );

        if (!File.Exists(filePath))
        {
            return;
        }

        string json =
            File.ReadAllText(filePath);

        BestProgressSave save =
            JsonUtility.FromJson<BestProgressSave>(
                json
            );

        if (save == null ||
            save.genome == null ||
            save.genome.network == null ||
            save.minimumPelletsRemaining < 0)
        {
            return;
        }

        SanitizeNetwork(
            save.genome.network
        );

        if (bestCompletedGenome != null &&
            AreGenomesStructurallyEqual(
                save.genome,
                bestCompletedGenome
            ))
        {
            return;
        }

        bestAlternativeProgressGenome =
            save.genome;

        bestAlternativeProgressMinimumPelletsRemaining =
            save.minimumPelletsRemaining;
    }

    private void SaveBestProgressGenome()
    {
        if (bestProgressGenome == null ||
            bestProgressGenome.network == null ||
            bestProgressMinimumPelletsRemaining == int.MaxValue)
        {
            return;
        }

        BestProgressSave save =
            new BestProgressSave();

        save.minimumPelletsRemaining =
            bestProgressMinimumPelletsRemaining;

        save.genome =
            bestProgressGenome;

        string json =
            JsonUtility.ToJson(
                save,
                true
            );

        File.WriteAllText(
            Path.Combine(
                saveFolderPath,
                BestProgressGenomeFileName
            ),
            json
        );
    }

    private void SaveBestAlternativeProgressGenome()
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                BestAlternativeProgressGenomeFileName
            );

        if (bestAlternativeProgressGenome == null ||
            bestAlternativeProgressGenome.network == null ||
            bestAlternativeProgressMinimumPelletsRemaining == int.MaxValue)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return;
        }

        BestProgressSave save =
            new BestProgressSave();

        save.minimumPelletsRemaining =
            bestAlternativeProgressMinimumPelletsRemaining;

        save.genome =
            bestAlternativeProgressGenome;

        string json =
            JsonUtility.ToJson(
                save,
                true
            );

        File.WriteAllText(
            filePath,
            json
        );
    }

    private float FindHistoricalBestFromGenerations()
    {
        if (!Directory.Exists(
                saveFolderPath))
        {
            return float.MinValue;
        }

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*_top10.json"
            );

        float best =
            float.MinValue;

        for (int i = 0;
             i < files.Length;
             i++)
        {
            string json =
                File.ReadAllText(files[i]);

            GenerationSave save =
                JsonUtility.FromJson<GenerationSave>(
                    json
                );

            if (save == null ||
                save.genomes == null ||
                !json.Contains("\"ghostCount\"") ||
                save.ghostCount != stageGhostCount)
            {
                continue;
            }

            for (int j = 0;
                 j < save.genomes.Count;
                 j++)
            {
                if (save.genomes[j] != null &&
                    save.genomes[j].fitness > best)
                {
                    best =
                        save.genomes[j].fitness;
                }
            }
        }

        return best;
    }

    private void LoadGenerationHistory()
    {
        generationHistory =
            new List<GenerationResult>();

        string filePath =
            Path.Combine(
                saveFolderPath,
                GenerationHistoryFileName
            );

        if (File.Exists(filePath))
        {
            string json =
                File.ReadAllText(filePath);

            GenerationHistorySave save =
                JsonUtility.FromJson<GenerationHistorySave>(
                    json
                );

            if (save != null &&
                save.generations != null)
            {
                generationHistory =
                    save.generations;

                bool historyHasGhostCount =
                    json.Contains("\"ghostCount\"");

                if (!historyHasGhostCount)
                {
                    for (int i = 0;
                         i < generationHistory.Count;
                         i++)
                    {
                        if (generationHistory[i] != null)
                        {
                            generationHistory[i].ghostCount = -1;
                        }
                    }
                }
                else
                {
                    for (int i = 0;
                         i < generationHistory.Count;
                         i++)
                    {
                        if (generationHistory[i] != null &&
                            generationHistory[i].ghostCount == 0)
                        {
                            generationHistory[i].ghostCount = -1;
                        }
                    }
                }
            }
        }

        RepairGenerationHistory();

        if (Directory.Exists(saveFolderPath))
        {
            SaveGenerationHistory();
        }
    }

    private void RepairGenerationHistory()
    {
        if (generationHistory == null)
        {
            generationHistory =
                new List<GenerationResult>();
        }

        if (!Directory.Exists(saveFolderPath))
        {
            return;
        }

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*_top10.json"
            );

        for (int i = 0;
             i < files.Length;
             i++)
        {
            string json =
                File.ReadAllText(files[i]);

            GenerationSave save =
                JsonUtility.FromJson<GenerationSave>(
                    json
                );

            if (save == null)
            {
                continue;
            }

            float maxFitness =
                float.MinValue;

            if (save.genomes != null)
            {
                for (int j = 0;
                     j < save.genomes.Count;
                     j++)
                {
                    Genome genome =
                        save.genomes[j];

                    if (genome != null &&
                        genome.fitness > maxFitness)
                    {
                        maxFitness =
                            genome.fitness;
                    }
                }
            }

            if (save.maxFitness > maxFitness)
            {
                maxFitness =
                    save.maxFitness;
            }

            bool hasCompletionCount =
                json.Contains("\"completionCount\"");

            bool hasBestCompletionTime =
                json.Contains("\"bestCompletionTime\"");

            bool hasMinimumPelletsRemaining =
                json.Contains("\"minimumPelletsRemaining\"");

            bool hasGhostCount =
                json.Contains("\"ghostCount\"");

            GenerationResult existing =
                null;

            for (int j = 0;
                 j < generationHistory.Count;
                 j++)
            {
                if (generationHistory[j] != null &&
                    generationHistory[j].generation ==
                    save.generation)
                {
                    existing =
                        generationHistory[j];
                    break;
                }
            }

            bool hasUsableFitness =
                maxFitness != float.MinValue &&
                maxFitness > 0f;

            if (save.maxFitness > 0f)
            {
                hasUsableFitness = true;
                maxFitness = Mathf.Max(
                    maxFitness,
                    save.maxFitness
                );
            }

            if (existing == null)
            {
                GenerationResult result =
                    new GenerationResult();

                result.generation =
                    save.generation;

                result.ghostCount =
                    hasGhostCount
                        ? save.ghostCount
                        : -1;

                result.maxFitness =
                    hasUsableFitness
                        ? maxFitness
                        : 0f;

                result.completed =
                    save.completed;

                result.completionCount =
                    hasCompletionCount
                        ? save.completionCount
                        : (save.completed ? -1 : 0);

                result.bestCompletionTime =
                    hasBestCompletionTime
                        ? save.bestCompletionTime
                        : (save.completed ? -1f : -1f);

                result.minimumPelletsRemaining =
                    hasMinimumPelletsRemaining
                        ? ((!save.completed &&
                            save.minimumPelletsRemaining == 0)
                            ? -1
                            : save.minimumPelletsRemaining)
                        : -1;

                generationHistory.Add(result);
            }
            else
            {
                if (hasGhostCount)
                {
                    existing.ghostCount = save.ghostCount;
                }
                else if (existing.ghostCount == 0)
                {
                    existing.ghostCount = -1;
                }

                if (hasUsableFitness &&
                    (existing.maxFitness <= 0f ||
                     maxFitness > existing.maxFitness))
                {
                    existing.maxFitness =
                        maxFitness;
                }

                if (save.completed)
                {
                    existing.completed = true;
                }

                if (hasCompletionCount)
                {
                    existing.completionCount =
                        save.completionCount;
                }
                else if (existing.completed &&
                         existing.completionCount == 0)
                {
                    existing.completionCount = -1;
                }

                if (hasBestCompletionTime)
                {
                    existing.bestCompletionTime =
                        save.bestCompletionTime;
                }
                else if (existing.completed &&
                         existing.bestCompletionTime == 0f)
                {
                    existing.bestCompletionTime = -1f;
                }

                if (hasMinimumPelletsRemaining)
                {
                    existing.minimumPelletsRemaining =
                        (!save.completed &&
                         save.minimumPelletsRemaining == 0)
                            ? -1
                            : save.minimumPelletsRemaining;
                }
                else if (existing.minimumPelletsRemaining == 0)
                {
                    existing.minimumPelletsRemaining = -1;
                }
            }
        }

        generationHistory.RemoveAll(
            item => item == null
        );

        generationHistory.Sort(
            (a, b) =>
                a.generation.CompareTo(
                    b.generation
                )
        );
    }

    private void SaveGenerationHistory()
    {
        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        GenerationHistorySave save =
            new GenerationHistorySave();

        save.generations =
            generationHistory;

        string json =
            JsonUtility.ToJson(
                save,
                true
            );

        string filePath =
            Path.Combine(
                saveFolderPath,
                GenerationHistoryFileName
            );

        string tempPath =
            filePath + ".tmp";

        File.WriteAllText(
            tempPath,
            json
        );

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        File.Move(
            tempPath,
            filePath
        );
    }

    private void SaveGenerationResult()
    {
        LoadGenerationHistory();

        GenerationResult result =
            new GenerationResult();

        result.generation =
            generationCount;

        result.ghostCount =
            stageGhostCount;

        result.maxFitness =
            GetCurrentGenerationMaxFitness();

        result.completionCount =
            GetCurrentCompletedCount();

        result.completed =
            result.completionCount > 0;

        result.bestCompletionTime =
            GetCurrentBestCompletionTime();

        result.minimumPelletsRemaining =
            GetCurrentMinimumPelletsRemaining();

        bool updated = false;

        for (int i = 0;
             i < generationHistory.Count;
             i++)
        {
            if (generationHistory[i] != null &&
                generationHistory[i].generation ==
                result.generation)
            {
                generationHistory[i] =
                    result;

                updated = true;
                break;
            }
        }

        if (!updated)
        {
            generationHistory.Add(result);
        }

        generationHistory.Sort(
            (a, b) =>
                a.generation.CompareTo(
                    b.generation
                )
        );

        SaveGenerationHistory();
    }

    private void SaveHistoricalBestFitness()
    {
        HistoricalBestSave save =
            new HistoricalBestSave();

        save.bestFitness =
            historicalBestFitness;

        string json =
            JsonUtility.ToJson(
                save,
                true
            );

        string filePath =
            Path.Combine(
                saveFolderPath,
                HistoricalBestFileName
            );

        File.WriteAllText(
            filePath,
            json
        );
    }

    private void InitializePopulation()
    {
        population =
            new List<Genome>();

        if (useManualNetworkPopulation)
        {
            generationCount = 1;
            FillManualNetworkPopulation();
            return;
        }

        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        int latestGen =
            GetLatestGenerationNumber();

        int seedGen =
            GetBestSeedGenerationForCurrentStage();

        if (seedGen >= 0)
        {
            List<Genome> savedTop =
                LoadTopGenomes(
                    seedGen
                );

            if (savedTop != null &&
                savedTop.Count > 0)
            {
                BuildPopulationFromSavedTop(
                    savedTop,
                    false
                );

                generationCount =
                    latestGen >= 0
                        ? latestGen + 1
                        : seedGen + 1;

                if (seedGen != latestGen)
                {
                    Debug.Log(
                        "EvolutionManager: se inicia la fase de " +
                        stageGhostCount +
                        " fantasma(s) usando como punto de partida la población guardada en la generación " +
                        seedGen +
                        ", sin importar sus mejores genomas como soluciones completadoras de esta fase."
                    );
                }

                return;
            }
        }

        Genome savedProgressGenome =
            bestAlternativeProgressGenome;

        if (savedProgressGenome == null &&
            bestProgressGenome != null &&
            (bestCompletedGenome == null ||
             !AreGenomesStructurallyEqual(
                 bestProgressGenome,
                 bestCompletedGenome
             )))
        {
            savedProgressGenome = bestProgressGenome;
        }

        int savedCompletedCopies =
            bestCompletedGenome != null
                ? Mathf.Min(ProtectedCompletedCopies, populationSize)
                : 0;

        for (int i = 0;
             i < savedCompletedCopies &&
             population.Count < populationSize;
             i++)
        {
            population.Add(
                CloneGenome(bestCompletedGenome)
            );
        }

        int savedProgressCopies =
            savedProgressGenome != null
                ? Mathf.Min(
                    ProtectedProgressCopies,
                    populationSize - population.Count
                )
                : 0;

        for (int i = 0;
             i < savedProgressCopies &&
             population.Count < populationSize;
             i++)
        {
            population.Add(
                CloneGenome(savedProgressGenome)
            );
        }

        innovationHistory.Clear();
        nextInnovationNumber = 1;
        lastSpeciesCount = 0;

        int latestHistoryGeneration = -1;

        for (int i = 0;
             i < generationHistory.Count;
             i++)
        {
            if (generationHistory[i] != null &&
                generationHistory[i].generation > latestHistoryGeneration)
            {
                latestHistoryGeneration =
                    generationHistory[i].generation;
            }
        }

        generationCount =
            latestGen >= 0
                ? latestGen + 1
                : latestHistoryGeneration + 1;

        if (generationCount < 0)
        {
            generationCount = 0;
        }

        while (population.Count < populationSize)
        {
            Genome parent = null;

            if (bestCompletedGenome != null)
            {
                parent = bestCompletedGenome;
            }
            else if (savedProgressGenome != null)
            {
                parent = savedProgressGenome;
            }

            if (parent == null)
            {
                population.Add(CreateInitialGenome());
            }
            else
            {
                Genome child = CloneGenome(parent);
                Mutate(child);
                child.fitness = 0f;
                population.Add(child);
            }
        }
    }

    private void FillManualNetworkPopulation()
    {
        population = new List<Genome>();

        int count = Mathf.Max(1, populationSize);

        Genome template =
            ManualPacmanNetworkFactory.CreateGenome(
                manualNetworkJson
            );

        if (template == null || template.network == null)
        {
            Debug.LogError(
                "EvolutionManager: no se ha podido crear la red manual. " +
                "Asigna un JSON válido en Manual Network Json.",
                this
            );
            return;
        }

        for (int i = 0; i < count; i++)
        {
            population.Add(CloneGenome(template));
        }

        Debug.Log(
            "EvolutionManager: generación manual creada con " +
            population.Count +
            " copias independientes de la misma red neuronal JSON: " +
            (manualNetworkJson != null ? manualNetworkJson.name : "configuración predeterminada") +
            ".",
            this
        );
    }

    private void BuildPopulationFromSavedTop(
        List<Genome> savedTop,
        bool resetFitness)
    {
        int protectedCount =
            Mathf.Min(
                ProtectedCompletedCopies,
                populationSize
            );

        if (bestCompletedGenome != null)
        {
            for (int i = 0;
                 i < protectedCount &&
                 population.Count < populationSize;
                 i++)
            {
                population.Add(
                    CloneGenome(
                        bestCompletedGenome
                    )
                );
            }
        }

        int progressProtectedCount =
            Mathf.Min(
                ProtectedProgressCopies,
                populationSize - population.Count
            );

        Genome progressGenomeToProtect =
            bestAlternativeProgressGenome;

        if (progressGenomeToProtect == null &&
            bestProgressGenome != null &&
            (bestCompletedGenome == null ||
             !AreGenomesStructurallyEqual(
                 bestProgressGenome,
                 bestCompletedGenome
             )))
        {
            progressGenomeToProtect =
                bestProgressGenome;
        }

        if (progressGenomeToProtect != null)
        {
            for (int i = 0;
                 i < progressProtectedCount &&
                 population.Count < populationSize;
                 i++)
            {
                population.Add(
                    CloneGenome(
                        progressGenomeToProtect
                    )
                );
            }
        }

        int savedEliteCount =
            Mathf.Min(
                maxEliteSpecies,
                savedTop.Count,
                populationSize - population.Count
            );

        for (int i = 0;
             i < savedEliteCount;
             i++)
        {
            SanitizeNetwork(
                savedTop[i].network
            );

            population.Add(
                CloneGenome(
                    savedTop[i]
                )
            );
        }

        RegisterExistingInnovations(
            population
        );

        List<Genome> breedingPool =
            new List<Genome>();

        for (int i = 0;
             i < savedTop.Count;
             i++)
        {
            if (savedTop[i] != null &&
                savedTop[i].network != null)
            {
                SanitizeNetwork(
                    savedTop[i].network
                );

                breedingPool.Add(
                    savedTop[i]
                );
            }
        }

        if (bestCompletedGenome != null)
        {
            breedingPool.Add(
                bestCompletedGenome
            );
        }

        if (bestProgressGenome != null)
        {
            breedingPool.Add(
                bestProgressGenome
            );
        }

        if (bestAlternativeProgressGenome != null)
        {
            breedingPool.Add(
                bestAlternativeProgressGenome
            );
        }

        while (population.Count < populationSize)
        {
            Genome parentA =
                SelectParentByTournament(
                    breedingPool,
                    Mathf.Max(3, tournamentSize)
                );

            Genome parentB =
                SelectParentByTournament(
                    breedingPool,
                    Mathf.Max(3, tournamentSize)
                );

            Genome child;

            if (breedingPool.Count > 1 &&
                Random.value < crossoverRate)
            {
                child =
                    Crossover(
                        parentA,
                        parentB
                    );
            }
            else
            {
                child =
                    CloneGenome(parentA);
            }

            Mutate(child);
            child.fitness = 0f;
            population.Add(child);
        }

        if (resetFitness)
        {
            for (int i = 0;
                 i < population.Count;
                 i++)
            {
                population[i].fitness = 0f;
            }
        }
    }

    private Genome CreateInitialGenome()
    {
        NeuralNetwork network =
            new NeuralNetwork();

        for (int i = 0;
             i < ExistingInputCount;
             i++)
        {
            network.nodes.Add(
                new NEATNode(
                    i,
                    NEATNode.NodeType.Input,
                    0f
                )
            );
        }

        network.nodes.Add(
            new NEATNode(
                DirectionInputNodeId,
                NEATNode.NodeType.Input,
                0f
            )
        );

        for (int i = 0;
             i < OutputCount;
             i++)
        {
            network.nodes.Add(
                new NEATNode(
                    OutputStartNodeId + i,
                    NEATNode.NodeType.Output,
                    10f
                )
            );
        }

        for (int input = 0;
             input < ExistingInputCount;
             input++)
        {
            for (int output = 0;
                 output < OutputCount;
                 output++)
            {
                int outputId =
                    OutputStartNodeId + output;

                int innovation =
                    GetOrCreateInnovation(
                        input,
                        outputId
                    );

                network.connections.Add(
                    new NEATConnection(
                        FindNodeById(network, input),
                        FindNodeById(network, outputId),
                        Random.Range(
                            -1f,
                            1f
                        ),
                        innovation
                    )
                );
            }
        }

        EnsureDirectionInputConnections(network);

        return new Genome(network);
    }

    private void StartGeneration()
    {
        timer = 0f;

        if (spawner != null)
        {
            activeAgents =
                spawner.SpawnPopulation(
                    population
                );
        }
    }

    private void NextGeneration()
    {
        if (useManualNetworkPopulation)
        {
            generationCount++;
            FillManualNetworkPopulation();
            StartGeneration();
            return;
        }

        UpdateHistoricalBestFitness();
        UpdateBestCompletedGenome();
        UpdateBestProgressGenome();
        SaveGenerationResult();

        Genome best =
            GetBestGenome();

        SaveTopGenomes(
            generationCount
        );

        List<Species> species =
            SpeciatePopulation(
                population
            );

        lastSpeciesCount =
            species.Count;

        CalculateAdjustedFitness(
            species
        );

        species.Sort(
            (a, b) =>
            {
                float aBest =
                    GetBestGenome(
                        a.members
                    ).fitness;

                float bBest =
                    GetBestGenome(
                        b.members
                    ).fitness;

                return
                    bBest.CompareTo(
                        aBest
                    );
            }
        );

        List<Genome> newPopulation =
            new List<Genome>();

        if (bestCompletedGenome != null)
        {
            for (int i = 0;
                 i < ProtectedCompletedCopies &&
                 newPopulation.Count < populationSize;
                 i++)
            {
                newPopulation.Add(
                    CloneGenome(
                        bestCompletedGenome
                    )
                );
            }
        }

        Genome progressGenomeToProtect =
            bestAlternativeProgressGenome;

        if (progressGenomeToProtect == null &&
            bestProgressGenome != null &&
            (bestCompletedGenome == null ||
             !AreGenomesStructurallyEqual(
                 bestProgressGenome,
                 bestCompletedGenome
             )))
        {
            progressGenomeToProtect =
                bestProgressGenome;
        }

        if (progressGenomeToProtect != null)
        {
            for (int i = 0;
                 i < ProtectedProgressCopies &&
                 newPopulation.Count < populationSize;
                 i++)
            {
                newPopulation.Add(
                    CloneGenome(
                        progressGenomeToProtect
                    )
                );
            }
        }

        if (newPopulation.Count == 0 &&
            historicalBestGenome != null)
        {
            newPopulation.Add(
                CloneGenome(
                    historicalBestGenome
                )
            );
        }

        int remainingEliteSlots =
            populationSize -
            newPopulation.Count;

        int eliteSpeciesCount =
            Mathf.Min(
                maxEliteSpecies,
                species.Count,
                remainingEliteSlots
            );

        for (int i = 0;
             i < eliteSpeciesCount;
             i++)
        {
            Genome champion =
                GetBestGenome(
                    species[i].members
                );

            newPopulation.Add(
                CloneGenome(
                    champion
                )
            );
        }

        while (newPopulation.Count <
               populationSize)
        {
            Species selectedSpecies =
                SelectSpecies(
                    species
                );

            if (selectedSpecies == null ||
                selectedSpecies.members.Count == 0)
            {
                break;
            }

            Genome parentA =
                SelectParentByTournament(
                    selectedSpecies.members,
                    tournamentSize
                );

            Genome parentB =
                SelectParentByTournament(
                    selectedSpecies.members,
                    tournamentSize
                );

            Genome child;

            if (selectedSpecies.members.Count > 1 &&
                Random.value < crossoverRate)
            {
                child =
                    Crossover(
                        parentA,
                        parentB
                    );
            }
            else
            {
                child =
                    CloneGenome(
                        parentA
                    );
            }

            Mutate(child);
            newPopulation.Add(child);
        }

        while (newPopulation.Count <
               populationSize)
        {
            Genome child =
                CloneGenome(
                    best
                );

            Mutate(child);

            newPopulation.Add(child);
        }

        population =
            newPopulation;

        generationCount++;

        StartGeneration();
    }

    private List<Species> SpeciatePopulation(
        List<Genome> genomes)
    {
        List<Species> species =
            new List<Species>();

        int nextSpeciesId = 1;

        for (int i = 0;
             i < genomes.Count;
             i++)
        {
            Genome genome =
                genomes[i];

            bool assigned = false;

            for (int j = 0;
                 j < species.Count;
                 j++)
            {
                if (CompatibilityDistance(
                        genome,
                        species[j].representative
                    ) <= compatibilityThreshold)
                {
                    species[j].members.Add(
                        genome
                    );

                    assigned = true;
                    break;
                }
            }

            if (!assigned)
            {
                Species newSpecies =
                    new Species();

                newSpecies.id =
                    nextSpeciesId++;

                newSpecies.representative =
                    genome;

                newSpecies.members.Add(
                    genome
                );

                species.Add(
                    newSpecies
                );
            }
        }

        return species;
    }

    private void CalculateAdjustedFitness(
        List<Species> species)
    {
        float minFitness =
            float.MaxValue;

        for (int i = 0;
             i < population.Count;
             i++)
        {
            if (population[i].fitness <
                minFitness)
            {
                minFitness =
                    population[i].fitness;
            }
        }

        for (int i = 0;
             i < species.Count;
             i++)
        {
            species[i].totalAdjustedFitness =
                0f;

            for (int j = 0;
                 j < species[i].members.Count;
                 j++)
            {
                Genome genome =
                    species[i].members[j];

                float shiftedFitness =
                    genome.fitness -
                    minFitness +
                    1f;

                species[i].totalAdjustedFitness +=
                    shiftedFitness /
                    species[i].members.Count;
            }
        }
    }

    private Species SelectSpecies(
        List<Species> species)
    {
        if (species == null ||
            species.Count == 0)
        {
            return null;
        }

        float total = 0f;

        for (int i = 0;
             i < species.Count;
             i++)
        {
            total +=
                Mathf.Max(
                    0f,
                    species[i].totalAdjustedFitness
                );
        }

        if (total <= 0f)
        {
            return species[
                Random.Range(
                    0,
                    species.Count
                )
            ];
        }

        float randomValue =
            Random.value * total;

        float accumulated = 0f;

        for (int i = 0;
             i < species.Count;
             i++)
        {
            accumulated +=
                Mathf.Max(
                    0f,
                    species[i].totalAdjustedFitness
                );

            if (randomValue <=
                accumulated)
            {
                return species[i];
            }
        }

        return species[
            species.Count - 1
        ];
    }

    private Genome SelectParentByTournament(
        List<Genome> candidates,
        int size)
    {
        if (candidates == null ||
            candidates.Count == 0)
        {
            return null;
        }

        Genome best =
            null;

        int actualSize =
            Mathf.Max(
                1,
                size
            );

        for (int i = 0;
             i < actualSize;
             i++)
        {
            Genome candidate =
                candidates[
                    Random.Range(
                        0,
                        candidates.Count
                    )
                ];

            if (best == null ||
                candidate.fitness >
                best.fitness)
            {
                best =
                    candidate;
            }
        }

        return best;
    }

    private Genome Crossover(
        Genome firstParent,
        Genome secondParent)
    {
        if (firstParent == null)
        {
            return null;
        }

        if (secondParent == null)
        {
            return CloneGenome(
                firstParent
            );
        }

        Genome fitterParent =
            firstParent;

        if (secondParent.fitness >
            firstParent.fitness)
        {
            fitterParent =
                secondParent;
        }

        Dictionary<int, NEATConnection>
            firstGenes =
            GetConnectionDictionary(
                firstParent
            );

        Dictionary<int, NEATConnection>
            secondGenes =
            GetConnectionDictionary(
                secondParent
            );

        HashSet<int> innovationSet =
            new HashSet<int>();

        foreach (int innovation
                 in firstGenes.Keys)
        {
            innovationSet.Add(
                innovation
            );
        }

        foreach (int innovation
                 in secondGenes.Keys)
        {
            innovationSet.Add(
                innovation
            );
        }

        List<int> innovations =
            new List<int>(
                innovationSet
            );

        innovations.Sort();

        NeuralNetwork childNetwork =
            new NeuralNetwork();

        Dictionary<int, NEATNode>
            childNodes =
            new Dictionary<int, NEATNode>();

        AddParentNodesToChild(
            fitterParent,
            childNetwork,
            childNodes
        );

        bool equalFitness =
            Mathf.Abs(
                firstParent.fitness -
                secondParent.fitness
            ) < 0.0001f;

        for (int i = 0;
             i < innovations.Count;
             i++)
        {
            int innovation =
                innovations[i];

            bool hasFirst =
                firstGenes.TryGetValue(
                    innovation,
                    out NEATConnection firstGene
                );

            bool hasSecond =
                secondGenes.TryGetValue(
                    innovation,
                    out NEATConnection secondGene
                );

            NEATConnection selected =
                null;

            if (hasFirst && hasSecond)
            {
                selected =
                    Random.value < 0.5f
                        ? firstGene
                        : secondGene;
            }
            else if (
                hasFirst &&
                (equalFitness ||
                 fitterParent ==
                 firstParent))
            {
                selected =
                    firstGene;
            }
            else if (
                hasSecond &&
                (equalFitness ||
                 fitterParent ==
                 secondParent))
            {
                selected =
                    secondGene;
            }

            if (selected == null)
            {
                continue;
            }

            AddNodeToChild(
                childNetwork,
                childNodes,
                selected.fromNode
            );

            AddNodeToChild(
                childNetwork,
                childNodes,
                selected.toNode
            );

            NEATConnection childConnection =
                new NEATConnection(
                    childNodes[
                        selected.fromNode.id
                    ],
                    childNodes[
                        selected.toNode.id
                    ],
                    selected.weight,
                    selected.innovation
                );

            if (hasFirst &&
                hasSecond &&
                (!firstGene.enabled ||
                 !secondGene.enabled))
            {
                childConnection.enabled =
                    Random.value >= 0.75f;
            }
            else
            {
                childConnection.enabled =
                    selected.enabled;
            }

            childNetwork.connections.Add(
                childConnection
            );
        }

        EnsureBasicNodes(
            childNetwork,
            childNodes
        );

        SanitizeNetwork(
            childNetwork
        );

        return new Genome(
            childNetwork
        );
    }

    private void AddParentNodesToChild(
        Genome parent,
        NeuralNetwork network,
        Dictionary<int, NEATNode> childNodes)
    {
        if (parent == null ||
            parent.network == null ||
            parent.network.nodes == null)
        {
            return;
        }

        for (int i = 0;
             i < parent.network.nodes.Count;
             i++)
        {
            AddNodeToChild(
                network,
                childNodes,
                parent.network.nodes[i]
            );
        }
    }

    private void AddNodeToChild(
        NeuralNetwork network,
        Dictionary<int, NEATNode> childNodes,
        NEATNode source)
    {
        if (source == null ||
            childNodes.ContainsKey(
                source.id))
        {
            return;
        }

        NEATNode node =
            new NEATNode(
                source.id,
                source.type,
                source.layer
            );

        childNodes.Add(
            source.id,
            node
        );

        network.nodes.Add(
            node
        );
    }

    private void EnsureBasicNodes(
        NeuralNetwork network,
        Dictionary<int, NEATNode> nodeMap)
    {
        for (int i = 0;
             i < ExistingInputCount;
             i++)
        {
            if (!nodeMap.ContainsKey(i))
            {
                NEATNode node =
                    new NEATNode(
                        i,
                        NEATNode.NodeType.Input,
                        0f
                    );

                nodeMap.Add(
                    i,
                    node
                );

                network.nodes.Add(
                    node
                );
            }
        }

        if (!nodeMap.ContainsKey(DirectionInputNodeId))
        {
            NEATNode node =
                new NEATNode(
                    DirectionInputNodeId,
                    NEATNode.NodeType.Input,
                    0f
                );

            nodeMap.Add(
                DirectionInputNodeId,
                node
            );

            network.nodes.Add(
                node
            );
        }

        for (int i = 0;
             i < OutputCount;
             i++)
        {
            int id =
                OutputStartNodeId + i;

            if (!nodeMap.ContainsKey(id))
            {
                NEATNode node =
                    new NEATNode(
                        id,
                        NEATNode.NodeType.Output,
                        10f
                    );

                nodeMap.Add(
                    id,
                    node
                );

                network.nodes.Add(
                    node
                );
            }
        }
    }

    private Genome GetBestGenome()
    {
        return GetBestGenome(
            population
        );
    }

    private Genome GetBestGenome(
        List<Genome> genomes)
    {
        if (genomes == null ||
            genomes.Count == 0)
        {
            return null;
        }

        Genome best =
            genomes[0];

        for (int i = 1;
             i < genomes.Count;
             i++)
        {
            if (genomes[i].fitness >
                best.fitness)
            {
                best =
                    genomes[i];
            }
        }

        return best;
    }

    private bool AreGenomesStructurallyEqual(
        Genome a,
        Genome b)
    {
        if (a == null ||
            b == null ||
            a.network == null ||
            b.network == null)
        {
            return false;
        }

        NeuralNetwork networkA =
            a.network;

        NeuralNetwork networkB =
            b.network;

        if (networkA.nodes == null ||
            networkB.nodes == null ||
            networkA.connections == null ||
            networkB.connections == null)
        {
            return false;
        }

        if (networkA.nodes.Count !=
            networkB.nodes.Count ||
            networkA.connections.Count !=
            networkB.connections.Count)
        {
            return false;
        }

        for (int i = 0;
             i < networkA.nodes.Count;
             i++)
        {
            NEATNode nodeA =
                networkA.nodes[i];

            NEATNode nodeB =
                networkB.nodes[i];

            if (nodeA == null ||
                nodeB == null)
            {
                return false;
            }

            if (nodeA.id != nodeB.id ||
                nodeA.type != nodeB.type ||
                nodeA.layer != nodeB.layer)
            {
                return false;
            }
        }

        for (int i = 0;
             i < networkA.connections.Count;
             i++)
        {
            NEATConnection connectionA =
                networkA.connections[i];

            NEATConnection connectionB =
                networkB.connections[i];

            if (connectionA == null ||
                connectionB == null ||
                connectionA.fromNode == null ||
                connectionA.toNode == null ||
                connectionB.fromNode == null ||
                connectionB.toNode == null)
            {
                return false;
            }

            if (connectionA.fromNode.id !=
                connectionB.fromNode.id ||
                connectionA.toNode.id !=
                connectionB.toNode.id ||
                connectionA.innovation !=
                connectionB.innovation ||
                connectionA.enabled !=
                connectionB.enabled ||
                connectionA.weight !=
                connectionB.weight)
            {
                return false;
            }
        }

        return true;
    }

    private Genome CloneGenome(
        Genome parent)
    {
        if (parent == null ||
            parent.network == null)
        {
            return new Genome(
                new NeuralNetwork()
            );
        }

        SanitizeNetwork(
            parent.network
        );

        NeuralNetwork newNetwork =
            new NeuralNetwork();

        newNetwork.manualNavigation =
            parent.network.manualNavigation;

        Dictionary<int, NEATNode>
            nodeMap =
            new Dictionary<int, NEATNode>();

        for (int i = 0;
             i < parent.network.nodes.Count;
             i++)
        {
            NEATNode node =
                parent.network.nodes[i];

            NEATNode newNode =
                new NEATNode(
                    node.id,
                    node.type,
                    node.layer
                );

            newNetwork.nodes.Add(
                newNode
            );

            nodeMap.Add(
                node.id,
                newNode
            );
        }

        for (int i = 0;
             i < parent.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                parent.network.connections[i];

            if (connection == null ||
                connection.fromNode == null ||
                connection.toNode == null ||
                !nodeMap.ContainsKey(
                    connection.fromNode.id) ||
                !nodeMap.ContainsKey(
                    connection.toNode.id))
            {
                continue;
            }

            NEATConnection newConnection =
                new NEATConnection(
                    nodeMap[
                        connection.fromNode.id
                    ],
                    nodeMap[
                        connection.toNode.id
                    ],
                    connection.weight,
                    connection.innovation
                );

            newConnection.enabled =
                connection.enabled;

            newNetwork.connections.Add(
                newConnection
            );
        }

        Genome clonedGenome =
            new Genome(
                newNetwork
            );

        clonedGenome.fitness =
            parent.fitness;

        return clonedGenome;
    }

    private void Mutate(
        Genome genome)
    {
        if (genome == null ||
            genome.network == null)
        {
            return;
        }

        for (int i = 0;
             i < genome.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                genome.network.connections[i];

            if (connection == null)
            {
                continue;
            }

            if (Random.value <
                weightMutationRate)
            {
                if (Random.value <
                    weightResetRate)
                {
                    connection.weight =
                        Random.Range(
                            -1f,
                            1f
                        );
                }
                else
                {
                    connection.weight +=
                        Random.Range(
                            -weightPerturbation,
                            weightPerturbation
                        );

                    connection.weight =
                        Mathf.Clamp(
                            connection.weight,
                            -3f,
                            3f
                        );
                }
            }
        }

        if (Random.value <
            reenableConnectionMutationRate)
        {
            MutateReenableConnection(
                genome
            );
        }

        if (Random.value <
            addConnectionMutationRate)
        {
            MutateAddConnection(
                genome
            );
        }

        if (Random.value <
            addNodeMutationRate)
        {
            MutateAddNode(
                genome
            );
        }
    }

    private void MutateReenableConnection(
        Genome genome)
    {
        List<NEATConnection>
            disabledConnections =
            new List<NEATConnection>();

        for (int i = 0;
             i < genome.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                genome.network.connections[i];

            if (connection != null &&
                !connection.enabled)
            {
                disabledConnections.Add(
                    connection
                );
            }
        }

        if (disabledConnections.Count ==
            0)
        {
            return;
        }

        disabledConnections[
            Random.Range(
                0,
                disabledConnections.Count
            )
        ].enabled = true;
    }

    private void MutateAddConnection(
        Genome genome)
    {
        if (genome.network.nodes == null ||
            genome.network.nodes.Count < 2)
        {
            return;
        }

        for (int attempt = 0;
             attempt < 100;
             attempt++)
        {
            NEATNode from =
                genome.network.nodes[
                    Random.Range(
                        0,
                        genome.network.nodes.Count
                    )
                ];

            NEATNode to =
                genome.network.nodes[
                    Random.Range(
                        0,
                        genome.network.nodes.Count
                    )
                ];

            if (from == null ||
                to == null)
            {
                continue;
            }

            if (from.id == to.id)
            {
                continue;
            }

            if (from.type ==
                NEATNode.NodeType.Output)
            {
                continue;
            }

            if (to.type ==
                NEATNode.NodeType.Input)
            {
                continue;
            }

            if (from.layer >= to.layer)
            {
                continue;
            }

            if (ConnectionExists(
                    genome,
                    from.id,
                    to.id))
            {
                continue;
            }

            int innovation =
                GetOrCreateInnovation(
                    from.id,
                    to.id
                );

            genome.network.connections.Add(
                new NEATConnection(
                    from,
                    to,
                    Random.Range(
                        -1f,
                        1f
                    ),
                    innovation
                )
            );

            return;
        }
    }

    private void MutateAddNode(
        Genome genome)
    {
        List<NEATConnection>
            enabledConnections =
            new List<NEATConnection>();

        for (int i = 0;
             i < genome.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                genome.network.connections[i];

            if (connection == null ||
                !connection.enabled ||
                connection.fromNode == null ||
                connection.toNode == null)
            {
                continue;
            }

            if (connection.fromNode.id ==
                connection.toNode.id)
            {
                continue;
            }

            if (connection.fromNode.layer >=
                connection.toNode.layer)
            {
                continue;
            }

            enabledConnections.Add(
                connection
            );
        }

        if (enabledConnections.Count == 0)
        {
            return;
        }

        NEATConnection selected =
            enabledConnections[
                Random.Range(
                    0,
                    enabledConnections.Count
                )
            ];

        if (selected.innovation <= 0)
        {
            selected.innovation =
                GetOrCreateInnovation(
                    selected.fromNode.id,
                    selected.toNode.id
                );
        }

        int newNodeId =
            1000000 +
            selected.innovation;

        if (FindNodeById(
                genome.network,
                newNodeId) != null)
        {
            return;
        }

        selected.enabled = false;

        float newLayer =
            (selected.fromNode.layer +
             selected.toNode.layer) *
            0.5f;

        NEATNode newNode =
            new NEATNode(
                newNodeId,
                NEATNode.NodeType.Hidden,
                newLayer
            );

        genome.network.nodes.Add(
            newNode
        );

        int firstInnovation =
            GetOrCreateInnovation(
                selected.fromNode.id,
                newNode.id
            );

        int secondInnovation =
            GetOrCreateInnovation(
                newNode.id,
                selected.toNode.id
            );

        genome.network.connections.Add(
            new NEATConnection(
                selected.fromNode,
                newNode,
                1f,
                firstInnovation
            )
        );

        genome.network.connections.Add(
            new NEATConnection(
                newNode,
                selected.toNode,
                selected.weight,
                secondInnovation
            )
        );
    }

    private bool ConnectionExists(
        Genome genome,
        int fromId,
        int toId)
    {
        if (genome == null)
        {
            return false;
        }

        return ConnectionExists(
            genome.network,
            fromId,
            toId
        );
    }

    private bool ConnectionExists(
        NeuralNetwork network,
        int fromId,
        int toId)
    {
        if (network == null ||
            network.connections == null)
        {
            return false;
        }

        for (int i = 0;
             i < network.connections.Count;
             i++)
        {
            NEATConnection connection =
                network.connections[i];

            if (connection == null ||
                connection.fromNode == null ||
                connection.toNode == null)
            {
                continue;
            }

            if (connection.fromNode.id ==
                    fromId &&
                connection.toNode.id ==
                    toId)
            {
                return true;
            }
        }

        return false;
    }

    private Dictionary<int, NEATConnection>
        GetConnectionDictionary(
            Genome genome)
    {
        Dictionary<int, NEATConnection>
            result =
            new Dictionary<int, NEATConnection>();

        if (genome == null ||
            genome.network == null ||
            genome.network.connections == null)
        {
            return result;
        }

        for (int i = 0;
             i < genome.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                genome.network.connections[i];

            if (connection == null ||
                connection.innovation <= 0)
            {
                continue;
            }

            if (!result.ContainsKey(
                    connection.innovation))
            {
                result.Add(
                    connection.innovation,
                    connection
                );
            }
        }

        return result;
    }

    private float CompatibilityDistance(
        Genome first,
        Genome second)
    {
        Dictionary<int, NEATConnection>
            firstGenes =
            GetConnectionDictionary(
                first
            );

        Dictionary<int, NEATConnection>
            secondGenes =
            GetConnectionDictionary(
                second
            );

        if (firstGenes.Count == 0 &&
            secondGenes.Count == 0)
        {
            return 0f;
        }

        HashSet<int> allInnovations =
            new HashSet<int>();

        foreach (int innovation
                 in firstGenes.Keys)
        {
            allInnovations.Add(
                innovation
            );
        }

        foreach (int innovation
                 in secondGenes.Keys)
        {
            allInnovations.Add(
                innovation
            );
        }

        List<int> innovations =
            new List<int>(
                allInnovations
            );

        innovations.Sort();

        int maxFirst =
            GetMaxInnovation(
                firstGenes
            );

        int maxSecond =
            GetMaxInnovation(
                secondGenes
            );

        int commonMax =
            Mathf.Min(
                maxFirst,
                maxSecond
            );

        int excess = 0;
        int disjoint = 0;
        int matching = 0;

        float weightDifference = 0f;

        for (int i = 0;
             i < innovations.Count;
             i++)
        {
            int innovation =
                innovations[i];

            bool hasFirst =
                firstGenes.ContainsKey(
                    innovation
                );

            bool hasSecond =
                secondGenes.ContainsKey(
                    innovation
                );

            if (hasFirst && hasSecond)
            {
                matching++;

                weightDifference +=
                    Mathf.Abs(
                        firstGenes[innovation].weight -
                        secondGenes[innovation].weight
                    );
            }
            else if (
                innovation > commonMax)
            {
                excess++;
            }
            else
            {
                disjoint++;
            }
        }

        float averageWeightDifference =
            matching > 0
                ? weightDifference / matching
                : 0f;

        int normalizer =
            Mathf.Max(
                Mathf.Max(
                    firstGenes.Count,
                    secondGenes.Count
                ),
                1
            );

        return
            excessCoefficient *
            excess /
            normalizer
            +
            disjointCoefficient *
            disjoint /
            normalizer
            +
            weightDifferenceCoefficient *
            averageWeightDifference;
    }

    private int GetMaxInnovation(
        Dictionary<int, NEATConnection> genes)
    {
        int max = 0;

        foreach (int innovation
                 in genes.Keys)
        {
            if (innovation > max)
            {
                max = innovation;
            }
        }

        return max;
    }

    private int GetOrCreateInnovation(
        int fromId,
        int toId)
    {
        string key =
            fromId +
            "->" +
            toId;

        if (innovationHistory.TryGetValue(
                key,
                out int existingInnovation))
        {
            return existingInnovation;
        }

        int newInnovation =
            nextInnovationNumber++;

        innovationHistory.Add(
            key,
            newInnovation
        );

        return newInnovation;
    }

    private void RegisterExistingInnovations(
        List<Genome> genomes)
    {
        for (int i = 0;
             i < genomes.Count;
             i++)
        {
            if (genomes[i] == null ||
                genomes[i].network == null ||
                genomes[i].network.connections == null)
            {
                continue;
            }

            for (int j = 0;
                 j < genomes[i].network.connections.Count;
                 j++)
            {
                NEATConnection connection =
                    genomes[i].network.connections[j];

                if (connection == null ||
                    connection.fromNode == null ||
                    connection.toNode == null)
                {
                    continue;
                }

                if (connection.innovation <= 0)
                {
                    connection.innovation =
                        GetOrCreateInnovation(
                            connection.fromNode.id,
                            connection.toNode.id
                        );
                }
                else
                {
                    string key =
                        connection.fromNode.id +
                        "->" +
                        connection.toNode.id;

                    if (!innovationHistory.ContainsKey(
                            key))
                    {
                        innovationHistory.Add(
                            key,
                            connection.innovation
                        );
                    }

                    if (connection.innovation >=
                        nextInnovationNumber)
                    {
                        nextInnovationNumber =
                            connection.innovation + 1;
                    }
                }
            }
        }
    }

    private void SanitizeNetwork(
        NeuralNetwork network)
    {
        if (network == null)
        {
            return;
        }

        if (network.nodes == null)
        {
            network.nodes =
                new List<NEATNode>();
        }

        if (network.connections == null)
        {
            network.connections =
                new List<NEATConnection>();
        }

        Dictionary<int, NEATNode>
            nodeMap =
            new Dictionary<int, NEATNode>();

        for (int i = 0;
             i < network.nodes.Count;
             i++)
        {
            NEATNode node =
                network.nodes[i];

            if (node == null)
            {
                continue;
            }

            if (node.type ==
                NEATNode.NodeType.Input)
            {
                node.layer = 0f;
            }
            else if (
                node.type ==
                NEATNode.NodeType.Output)
            {
                node.layer = 10f;
            }

            if (!nodeMap.ContainsKey(
                    node.id))
            {
                nodeMap.Add(
                    node.id,
                    node
                );
            }
        }

        for (int i = 0;
             i < network.connections.Count;
             i++)
        {
            NEATConnection connection =
                network.connections[i];

            if (connection == null ||
                connection.fromNode == null ||
                connection.toNode == null)
            {
                continue;
            }

            if (nodeMap.TryGetValue(
                    connection.fromNode.id,
                    out NEATNode fromNode))
            {
                connection.fromNode =
                    fromNode;
            }

            if (nodeMap.TryGetValue(
                    connection.toNode.id,
                    out NEATNode toNode))
            {
                connection.toNode =
                    toNode;
            }

            if (connection.innovation <= 0 &&
                connection.fromNode != null &&
                connection.toNode != null)
            {
                connection.innovation =
                    GetOrCreateInnovation(
                        connection.fromNode.id,
                        connection.toNode.id
                    );
            }
        }

        if (!network.manualNavigation)
        {
            EnsureBasicNodes(
                network,
                nodeMap
            );

            EnsureDirectionInputConnections(network);
        }
    }

    private void EnsureDirectionInputConnections(
        NeuralNetwork network)
    {
        if (network == null ||
            network.nodes == null)
        {
            return;
        }

        NEATNode directionInput =
            FindNodeById(
                network,
                DirectionInputNodeId
            );

        if (directionInput == null)
        {
            return;
        }

        for (int i = 0;
             i < OutputCount;
             i++)
        {
            int outputId =
                OutputStartNodeId + i;

            NEATNode outputNode =
                FindNodeById(
                    network,
                    outputId
                );

            if (outputNode == null ||
                ConnectionExists(
                    network,
                    DirectionInputNodeId,
                    outputId
                ))
            {
                continue;
            }

            int innovation =
                GetOrCreateInnovation(
                    DirectionInputNodeId,
                    outputId
                );

            network.connections.Add(
                new NEATConnection(
                    directionInput,
                    outputNode,
                    Random.Range(
                        -1f,
                        1f
                    ),
                    innovation
                )
            );
        }
    }

    private NEATNode FindNodeById(
        NeuralNetwork network,
        int id)
    {
        for (int i = 0;
             i < network.nodes.Count;
             i++)
        {
            if (network.nodes[i] != null &&
                network.nodes[i].id == id)
            {
                return network.nodes[i];
            }
        }

        return null;
    }

    private NEATNode FindNodeById(
        Genome genome,
        int id)
    {
        if (genome == null)
        {
            return null;
        }

        return FindNodeById(
            genome.network,
            id
        );
    }

    private void SaveTopGenomes(
        int genNumber)
    {
        List<Genome> sorted =
            new List<Genome>(
                population
            );

        sorted.Sort(
            (a, b) =>
                b.fitness.CompareTo(
                    a.fitness
                )
        );

        int count =
            Mathf.Min(
                topGenomesToSave,
                sorted.Count
            );

        GenerationSave save =
            new GenerationSave();

        save.generation =
            genNumber;

        save.ghostCount =
            stageGhostCount;

        save.maxFitness =
            GetCurrentGenerationMaxFitness();

        save.completionCount =
            GetCurrentCompletedCount();

        save.completed =
            save.completionCount > 0;

        save.bestCompletionTime =
            GetCurrentBestCompletionTime();

        save.minimumPelletsRemaining =
            GetCurrentMinimumPelletsRemaining();

        for (int i = 0;
             i < count;
             i++)
        {
            Genome savedGenome =
                CloneGenome(
                    sorted[i]
                );

            savedGenome.fitness =
                sorted[i].fitness;

            save.genomes.Add(
                savedGenome
            );
        }

        string filePath =
            Path.Combine(
                saveFolderPath,
                "gen_" +
                genNumber.ToString("D5") +
                "_top10.json"
            );

        string json =
            JsonUtility.ToJson(
                save,
                true
            );

        File.WriteAllText(
            filePath,
            json
        );
    }

    private List<Genome> LoadTopGenomes(
        int genNumber)
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                "gen_" +
                genNumber.ToString("D5") +
                "_top10.json"
            );

        if (!File.Exists(filePath))
        {
            return null;
        }

        string json =
            File.ReadAllText(
                filePath
            );

        GenerationSave save =
            JsonUtility.FromJson<GenerationSave>(
                json
            );

        if (save == null ||
            save.genomes == null)
        {
            return null;
        }

        return save.genomes;
    }

    private int GetBestSeedGenerationForCurrentStage()
    {
        if (!Directory.Exists(saveFolderPath))
        {
            return -1;
        }

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*_top10.json"
            );

        int latestSameStage = -1;
        int closestLowerStage = -1;
        int latestClosestLowerStage = -1;
        int latestLegacy = -1;
        int latestHigherStage = -1;
        int latestAny = -1;

        for (int i = 0; i < files.Length; i++)
        {
            if (!TryGetGenerationNumber(files[i], out int generation))
            {
                continue;
            }

            if (generation > latestAny)
            {
                latestAny = generation;
            }

            string json = File.ReadAllText(files[i]);
            GenerationSave save =
                JsonUtility.FromJson<GenerationSave>(json);

            if (save == null || save.genomes == null || save.genomes.Count == 0)
            {
                continue;
            }

            int savedGhostCount =
                json.Contains("\"ghostCount\"")
                    ? save.ghostCount
                    : -1;

            if (savedGhostCount == stageGhostCount)
            {
                if (generation > latestSameStage)
                {
                    latestSameStage = generation;
                }
            }
            else if (savedGhostCount >= 0 &&
                     savedGhostCount < stageGhostCount)
            {
                if (savedGhostCount > closestLowerStage ||
                    (savedGhostCount == closestLowerStage &&
                     generation > latestClosestLowerStage))
                {
                    closestLowerStage = savedGhostCount;
                    latestClosestLowerStage = generation;
                }
            }
            else if (savedGhostCount < 0)
            {
                if (generation > latestLegacy)
                {
                    latestLegacy = generation;
                }
            }
            else if (savedGhostCount > stageGhostCount)
            {
                if (generation > latestHigherStage)
                {
                    latestHigherStage = generation;
                }
            }
        }

        if (latestSameStage >= 0)
        {
            return latestSameStage;
        }

        if (latestClosestLowerStage >= 0)
        {
            return latestClosestLowerStage;
        }

        if (latestLegacy >= 0)
        {
            return latestLegacy;
        }

        if (latestHigherStage >= 0)
        {
            Debug.LogWarning(
                "EvolutionManager: no se encontraron genomas guardados para esta fase ni para una fase más sencilla. Se usará como punto de partida una población de una fase con más fantasmas, sin considerarla solución completadora actual."
            );
            return latestHigherStage;
        }

        return latestAny;
    }

    private bool TryGetGenerationNumber(
        string filePath,
        out int generation)
    {
        generation = -1;

        string fileName =
            Path.GetFileNameWithoutExtension(filePath);

        if (!fileName.StartsWith("gen_") ||
            !fileName.EndsWith("_top10"))
        {
            return false;
        }

        string numberPart =
            fileName.Substring(
                4,
                fileName.Length - 10
            );

        return int.TryParse(numberPart, out generation);
    }

    private int GetLatestGenerationNumber()
    {
        if (!Directory.Exists(
                saveFolderPath))
        {
            return 0;
        }

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*_top10.json"
            );

        int maxGeneration = -1;

        for (int i = 0;
             i < files.Length;
             i++)
        {
            if (TryGetGenerationNumber(
                    files[i],
                    out int generation))
            {
                if (generation > maxGeneration)
                {
                    maxGeneration = generation;
                }
            }
        }

        return maxGeneration;
    }

    private bool AllAgentsDead()
    {
        int count = 0;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null &&
                activeAgents[i].gameObject.activeSelf)
            {
                count++;
            }
        }

        return count == 0;
    }

    public int GetAliveCount()
    {
        int count = 0;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null &&
                activeAgents[i].gameObject.activeSelf)
            {
                count++;
            }
        }

        return count;
    }

    private void UpdateLeaderHighlight()
    {
        if (activeAgents == null ||
            activeAgents.Count == 0)
        {
            return;
        }

        AgentController currentLeader =
            null;

        float maxFitness =
            float.MinValue;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null &&
                activeAgents[i].gameObject.activeSelf)
            {
                float agentFitness =
                    activeAgents[i].GetFitness();

                if (agentFitness >
                    maxFitness)
                {
                    maxFitness =
                        agentFitness;

                    currentLeader =
                        activeAgents[i];
                }
            }
        }

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null)
            {
                activeAgents[i].SetHighlight(
                    activeAgents[i] ==
                    currentLeader
                );
            }
        }
    }
}