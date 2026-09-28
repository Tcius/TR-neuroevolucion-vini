using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EvolutionManager : MonoBehaviour
{
    [Header("Evolution Settings")]
    public int populationSize = 20;
    public float generationDuration = 20f;

    [Header("NEAT Settings")]
    [Range(0f, 1f)] public float crossoverRate = 0.75f;
    [Range(0f, 1f)] public float weightMutationRate = 0.15f;
    [Range(0f, 1f)] public float addConnectionMutationRate = 0.05f;
    [Range(0f, 1f)] public float addNodeMutationRate = 0.05f;
    [Range(0f, 1f)] public float reenableConnectionMutationRate = 0.02f;
    [Range(0f, 1f)] public float weightResetRate = 0.10f;

    public float weightPerturbation = 0.25f;

    [Header("Speciation")]
    public float compatibilityThreshold = 3f;
    public float excessCoefficient = 1f;
    public float disjointCoefficient = 1f;
    public float weightDifferenceCoefficient = 0.4f;
    public int maxEliteSpecies = 4;
    public int tournamentSize = 2;
    public int topGenomesToSave = 10;

    [HideInInspector] public List<Genome> population =
        new List<Genome>();

    [HideInInspector] public List<AgentController> activeAgents =
        new List<AgentController>();

    [HideInInspector] public int generationCount = 1;
    [HideInInspector] public float timer = 0f;

    private const string SaveFolderName =
        "Genomes_NEAT_Phase1_Completion_Continue";

    private const string PrimarySourceSaveFolderName =
        "Genomes_NEAT_v2";

    private const string SecondarySourceSaveFolderName =
        "Genomes_NEAT_Phase1_Completion";

    private const int ResumeFromGeneration = 300;

    private const string HistoricalBestFileName =
        "historical_best.json";

    private PopulationSpawner spawner;
    private string saveFolderPath;

    private readonly Dictionary<string, int>
        innovationHistory =
        new Dictionary<string, int>();

    private int nextInnovationNumber = 1;
    private int lastSpeciesCount = 0;

    private float historicalBestFitness =
        float.MinValue;

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
        public List<Genome> genomes =
            new List<Genome>();
    }

    [System.Serializable]
    private class HistoricalBestSave
    {
        public float bestFitness;
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

        if (!Directory.Exists(
                saveFolderPath))
        {
            Directory.CreateDirectory(
                saveFolderPath
            );
        }

        LoadHistoricalBestFitness();
    }

    private void Start()
    {
        spawner =
            FindAnyObjectByType<PopulationSpawner>();

        InitializePopulation();
        StartGeneration();
    }

    private void Update()
    {
        timer += Time.deltaTime;

        UpdateHistoricalBestFitness();
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

        float currentBest =
            GetCurrentGenerationMaxFitness();

        if (currentBest >
            historicalBestFitness)
        {
            historicalBestFitness =
                currentBest;

            SaveHistoricalBestFitness();
        }
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

                return;
            }
        }

        float bestFromGenerations =
            FindHistoricalBestFromGenerations();

        if (bestFromGenerations !=
            float.MinValue)
        {
            historicalBestFitness =
                bestFromGenerations;

            SaveHistoricalBestFitness();
        }
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
                save.genomes == null)
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

        int latestSavedGeneration =
            GetLatestGenerationNumber();

        if (latestSavedGeneration > 0)
        {
            List<Genome> savedTop =
                LoadTopGenomes(
                    latestSavedGeneration
                );

            if (savedTop != null &&
                savedTop.Count > 0)
            {
                LoadPopulationFromSavedTop(
                    savedTop,
                    latestSavedGeneration
                );

                return;
            }
        }

        List<Genome> resumeTop =
            LoadTopGenomesFromSource(
                PrimarySourceSaveFolderName,
                ResumeFromGeneration
            );

        if (resumeTop == null ||
            resumeTop.Count == 0)
        {
            resumeTop =
                LoadTopGenomesFromSource(
                    SecondarySourceSaveFolderName,
                    ResumeFromGeneration
                );
        }

        if (resumeTop != null &&
            resumeTop.Count > 0)
        {
            population =
                CreateResumePopulation(
                    resumeTop
                );

            RegisterExistingInnovations(
                population
            );

            generationCount =
                ResumeFromGeneration + 1;

            return;
        }

        generationCount = 1;

        for (int i = 0;
             i < populationSize;
             i++)
        {
            population.Add(
                CreateInitialGenome()
            );
        }
    }

    private void LoadPopulationFromSavedTop(
        List<Genome> savedTop,
        int savedGeneration)
    {
        for (int i = 0;
             i < savedTop.Count &&
             population.Count < populationSize;
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

        while (population.Count <
               populationSize)
        {
            Genome parentA =
                SelectParentByTournament(
                    population,
                    Mathf.Max(
                        2,
                        tournamentSize
                    )
                );

            Genome parentB =
                SelectParentByTournament(
                    population,
                    Mathf.Max(
                        2,
                        tournamentSize
                    )
                );

            Genome child;

            if (Random.value <
                    crossoverRate &&
                population.Count > 1)
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
            population.Add(child);
        }

        generationCount =
            savedGeneration + 1;
    }

    private List<Genome> CreateResumePopulation(
        List<Genome> savedTop)
    {
        List<Genome> result =
            new List<Genome>();

        int seedCount =
            Mathf.Min(
                savedTop.Count,
                populationSize
            );

        for (int i = 0;
             i < seedCount;
             i++)
        {
            Genome seed =
                CloneGenome(
                    savedTop[i]
                );

            seed.fitness = 0f;

            result.Add(seed);
        }

        int sourceIndex = 0;

        while (result.Count <
               populationSize)
        {
            Genome child =
                CloneGenome(
                    savedTop[
                        sourceIndex %
                        savedTop.Count
                    ]
                );

            child.fitness = 0f;

            Mutate(child);

            result.Add(child);

            sourceIndex++;
        }

        return result;
    }

    private List<Genome> LoadTopGenomesFromSource(
        string folderName,
        int generation)
    {
        string sourceFolderPath =
            Path.Combine(
                Application.persistentDataPath,
                folderName
            );

        string filePath =
            Path.Combine(
                sourceFolderPath,
                "gen_" +
                generation.ToString("D5") +
                "_top10.json"
            );

        if (!File.Exists(filePath))
        {
            return null;
        }

        string json =
            File.ReadAllText(filePath);

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

    private Genome CreateInitialGenome()
    {
        NeuralNetwork network =
            new NeuralNetwork();

        for (int i = 0;
             i < 13;
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

        for (int i = 0;
             i < 4;
             i++)
        {
            network.nodes.Add(
                new NEATNode(
                    13 + i,
                    NEATNode.NodeType.Output,
                    10f
                )
            );
        }

        for (int input = 0;
             input < 13;
             input++)
        {
            for (int output = 0;
                 output < 4;
                 output++)
            {
                int innovation =
                    GetOrCreateInnovation(
                        input,
                        13 + output
                    );

                network.connections.Add(
                    new NEATConnection(
                        network.nodes[input],
                        network.nodes[
                            13 + output
                        ],
                        Random.Range(
                            -1f,
                            1f
                        ),
                        innovation
                    )
                );
            }
        }

        return new Genome(
            network
        );
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
        UpdateHistoricalBestFitness();

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

        int eliteSpeciesCount =
            Mathf.Min(
                maxEliteSpecies,
                species.Count,
                populationSize
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
                Random.value <
                crossoverRate)
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
                CloneGenome(best);

            Mutate(child);

            newPopulation.Add(
                child
            );
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
             i < 13;
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

        for (int i = 0;
             i < 4;
             i++)
        {
            int id =
                13 + i;

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

        return new Genome(
            newNetwork
        );
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
        for (int i = 0;
             i < genome.network.connections.Count;
             i++)
        {
            NEATConnection connection =
                genome.network.connections[i];

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
                max =
                    innovation;
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

        EnsureBasicNodes(
            network,
            nodeMap
        );
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

        for (int i = 0;
             i < count;
             i++)
        {
            save.genomes.Add(
                CloneGenome(
                    sorted[i]
                )
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
            File.ReadAllText(filePath);

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

        int maxGeneration = 0;

        for (int i = 0;
             i < files.Length;
             i++)
        {
            string fileName =
                Path.GetFileNameWithoutExtension(
                    files[i]
                );

            if (!fileName.StartsWith(
                    "gen_") ||
                !fileName.EndsWith(
                    "_top10"))
            {
                continue;
            }

            string numberPart =
                fileName.Substring(
                    4,
                    fileName.Length -
                    10
                );

            if (int.TryParse(
                    numberPart,
                    out int generation))
            {
                if (generation >
                    maxGeneration)
                {
                    maxGeneration =
                        generation;
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
    public Genome GetBestGenome()
    {
        return GetBestGenome(population);
    }

    public Genome GetBestGenome(List<Genome> genomes)
    {
        if (genomes == null || genomes.Count == 0)
        {
            return null;
        }

        Genome best = genomes[0];

        for (int i = 1; i < genomes.Count; i++)
        {
            if (genomes[i].fitness > best.fitness)
            {
                best = genomes[i];
            }
        }

        return best;
    }
}