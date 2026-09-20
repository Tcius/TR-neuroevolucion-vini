using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EvolutionManager : MonoBehaviour
{
    [Header("Evolution Settings")]
    public int populationSize = 20;
    public float generationDuration = 20f;

    [Header("Mutation Settings")]
    public float weightMutationRate = 0.15f;
    public float addConnectionMutationRate = 0.05f;
    public float addNodeMutationRate = 0.02f;

    [HideInInspector] public List<Genome> population = new List<Genome>();
    [HideInInspector] public List<AgentController> activeAgents = new List<AgentController>();
    [HideInInspector] public int generationCount = 1;
    [HideInInspector] public float timer = 0f;

    private PopulationSpawner spawner;
    private string saveFolderPath;

    [System.Serializable]
    public class NEATNode
    {
        public enum NodeType { Input, Hidden, Output }
        public int id;
        public NodeType type;
        public float value;

        public NEATNode(int id, NodeType type)
        {
            this.id = id;
            this.type = type;
            this.value = 0f;
        }
    }

    [System.Serializable]
    public class NEATConnection
    {
        public NEATNode fromNode;
        public NEATNode toNode;
        public float weight;
        public bool enabled;

        public NEATConnection(NEATNode fromNode, NEATNode toNode, float weight)
        {
            this.fromNode = fromNode;
            this.toNode = toNode;
            this.weight = weight;
            this.enabled = true;
        }
    }

    [System.Serializable]
    public class NeuralNetwork
    {
        public List<NEATNode> nodes = new List<NEATNode>();
        public List<NEATConnection> connections = new List<NEATConnection>();

        public float[] FeedForward(float[] inputValues, int outputCount)
        {
            if (nodes == null || nodes.Count == 0)
                return new float[outputCount];

            List<NEATNode> inputNodes =
                nodes.FindAll(n => n.type == NEATNode.NodeType.Input);

            List<NEATNode> outputNodes =
                nodes.FindAll(n => n.type == NEATNode.NodeType.Output);

            List<NEATNode> hiddenNodes =
                nodes.FindAll(n => n.type == NEATNode.NodeType.Hidden);

            int inputsToAssign = Mathf.Min(
                inputValues.Length,
                inputNodes.Count
            );

            for (int i = 0; i < inputsToAssign; i++)
            {
                inputNodes[i].value = inputValues[i];
            }

            for (int i = 0; i < hiddenNodes.Count; i++)
            {
                float sum = 0f;

                if (connections != null)
                {
                    foreach (NEATConnection conn in connections)
                    {
                        if (conn != null &&
                            conn.enabled &&
                            conn.toNode != null &&
                            conn.toNode.id == hiddenNodes[i].id)
                        {
                            if (conn.fromNode != null)
                            {
                                sum += conn.fromNode.value * conn.weight;
                            }
                        }
                    }
                }

                hiddenNodes[i].value = (float)System.Math.Tanh(sum);
            }

            float[] outputs = new float[outputCount];

            for (int i = 0;
                 i < outputNodes.Count && i < outputCount;
                 i++)
            {
                float sum = 0f;

                if (connections != null)
                {
                    foreach (NEATConnection conn in connections)
                    {
                        if (conn != null &&
                            conn.enabled &&
                            conn.toNode != null &&
                            conn.toNode.id == outputNodes[i].id)
                        {
                            if (conn.fromNode != null)
                            {
                                sum += conn.fromNode.value * conn.weight;
                            }
                        }
                    }
                }

                outputNodes[i].value = (float)System.Math.Tanh(sum);
                outputs[i] = outputNodes[i].value;
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
            this.network = net;
            this.fitness = 0f;
        }
    }

    private void Awake()
    {
        saveFolderPath = Path.Combine(
            Application.persistentDataPath,
            "Genomes"
        );

        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }
    }

    private void Start()
    {
        spawner = FindAnyObjectByType<PopulationSpawner>();
        InitializePopulation();
        StartGeneration();
    }

    public float GetCurrentGenerationMaxFitness()
    {
        if (population == null || population.Count == 0)
            return 0f;

        float max = 0f;

        for (int i = 0; i < population.Count; i++)
        {
            if (population[i].fitness > max)
            {
                max = population[i].fitness;
            }
        }

        return max;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        UpdateLeaderHighlight();

        if (timer >= generationDuration || AllAgentsDead())
        {
            NextGeneration();
        }
    }

    private void InitializePopulation()
    {
        population = new List<Genome>();

        int latestGen = GetLatestGenerationNumber();

        if (latestGen > 0)
        {
            Genome savedChampion = LoadChampionGenome(latestGen);

            if (savedChampion != null &&
                savedChampion.network != null &&
                savedChampion.network.nodes != null)
            {
                generationCount = latestGen + 1;

                population.Add(savedChampion);

                while (population.Count < populationSize)
                {
                    Genome child = CloneGenome(savedChampion);
                    Mutate(child);
                    population.Add(child);
                }

                return;
            }
        }

        generationCount = 1;

        for (int i = 0; i < populationSize; i++)
        {
            NeuralNetwork net = new NeuralNetwork();

            for (int j = 0; j < 13; j++)
            {
                net.nodes.Add(
                    new NEATNode(
                        j,
                        NEATNode.NodeType.Input
                    )
                );
            }

            for (int k = 0; k < 4; k++)
            {
                net.nodes.Add(
                    new NEATNode(
                        13 + k,
                        NEATNode.NodeType.Output
                    )
                );
            }

            for (int j = 0; j < 13; j++)
            {
                for (int k = 0; k < 4; k++)
                {
                    float weight = Random.Range(-1f, 1f);

                    net.connections.Add(
                        new NEATConnection(
                            net.nodes[j],
                            net.nodes[13 + k],
                            weight
                        )
                    );
                }
            }

            population.Add(new Genome(net));
        }
    }

    private void StartGeneration()
    {
        timer = 0f;

        if (spawner != null)
        {
            activeAgents = spawner.SpawnPopulation(population);
        }
    }

    private void NextGeneration()
    {
        Genome best = GetBestGenome();

        SaveChampionGenome(
            best,
            generationCount
        );

        List<Genome> newPopulation =
            new List<Genome>();

        Genome championCopy =
            CloneGenome(best);

        newPopulation.Add(championCopy);

        while (newPopulation.Count < populationSize)
        {
            Genome parent =
                SelectParentByTournament(2);

            Genome child =
                CloneGenome(parent);

            Mutate(child);

            newPopulation.Add(child);
        }

        population = newPopulation;

        generationCount++;

        StartGeneration();
    }

    private Genome SelectParentByTournament(int tournamentSize)
    {
        Genome bestInTournament = null;

        for (int i = 0; i < tournamentSize; i++)
        {
            Genome randomGenome =
                population[
                    Random.Range(
                        0,
                        population.Count
                    )
                ];

            if (bestInTournament == null ||
                randomGenome.fitness >
                bestInTournament.fitness)
            {
                bestInTournament = randomGenome;
            }
        }

        return bestInTournament;
    }

    private Genome GetBestGenome()
    {
        Genome best = population[0];

        foreach (Genome genome in population)
        {
            if (genome.fitness > best.fitness)
            {
                best = genome;
            }
        }

        return best;
    }

    private Genome CloneGenome(Genome parent)
    {
        NeuralNetwork newNet =
            new NeuralNetwork();

        Dictionary<int, NEATNode> nodeMap =
            new Dictionary<int, NEATNode>();

        foreach (NEATNode node in parent.network.nodes)
        {
            NEATNode newNode =
                new NEATNode(
                    node.id,
                    node.type
                );

            newNet.nodes.Add(newNode);

            nodeMap.Add(
                node.id,
                newNode
            );
        }

        foreach (NEATConnection connection
                 in parent.network.connections)
        {
            if (nodeMap.ContainsKey(
                    connection.fromNode.id) &&
                nodeMap.ContainsKey(
                    connection.toNode.id))
            {
                NEATConnection newConnection =
                    new NEATConnection(
                        nodeMap[
                            connection.fromNode.id
                        ],
                        nodeMap[
                            connection.toNode.id
                        ],
                        connection.weight
                    );

                newConnection.enabled =
                    connection.enabled;

                newNet.connections.Add(
                    newConnection
                );
            }
        }

        return new Genome(newNet);
    }

    private void Mutate(Genome genome)
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
                continue;

            if (Random.Range(0f, 1f) <
                weightMutationRate)
            {
                if (Random.Range(0f, 1f) < 0.1f)
                {
                    connection.weight =
                        Random.Range(-1f, 1f);
                }
                else
                {
                    float change =
                        Random.Range(-0.25f, 0.25f);

                    connection.weight += change;

                    connection.weight =
                        Mathf.Clamp(
                            connection.weight,
                            -3f,
                            3f
                        );
                }
            }
        }

        if (Random.Range(0f, 1f) <
            addConnectionMutationRate)
        {
            MutateAddConnection(genome);
        }

        if (Random.Range(0f, 1f) <
            addNodeMutationRate)
        {
            MutateAddNode(genome);
        }
    }

    private void MutateAddConnection(Genome genome)
    {
        List<NEATNode> nodes =
            genome.network.nodes;

        if (nodes.Count < 2)
            return;

        NEATNode n1 =
            nodes[
                Random.Range(
                    0,
                    nodes.Count
                )
            ];

        NEATNode n2 =
            nodes[
                Random.Range(
                    0,
                    nodes.Count
                )
            ];

        if (n1.type ==
                NEATNode.NodeType.Output &&
            n2.type ==
                NEATNode.NodeType.Input)
        {
            NEATNode temp = n1;
            n1 = n2;
            n2 = temp;
        }

        bool exists =
            genome.network.connections.Exists(
                c =>
                    c.fromNode.id == n1.id &&
                    c.toNode.id == n2.id
            );

        if (!exists &&
            n1.id != n2.id)
        {
            genome.network.connections.Add(
                new NEATConnection(
                    n1,
                    n2,
                    Random.Range(-1f, 1f)
                )
            );
        }
    }

    private void MutateAddNode(Genome genome)
    {
        if (genome.network.connections.Count == 0)
            return;

        NEATConnection connection =
            genome.network.connections[
                Random.Range(
                    0,
                    genome.network.connections.Count
                )
            ];

        if (!connection.enabled)
            return;

        connection.enabled = false;

        int newId =
            genome.network.nodes.Count;

        NEATNode newNode =
            new NEATNode(
                newId,
                NEATNode.NodeType.Hidden
            );

        genome.network.nodes.Add(newNode);

        genome.network.connections.Add(
            new NEATConnection(
                connection.fromNode,
                newNode,
                1f
            )
        );

        genome.network.connections.Add(
            new NEATConnection(
                newNode,
                connection.toNode,
                connection.weight
            )
        );
    }

    private void SaveChampionGenome(
        Genome genome,
        int genNumber)
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                "gen_" + genNumber + ".json"
            );

        string json =
            JsonUtility.ToJson(
                genome,
                true
            );

        File.WriteAllText(
            filePath,
            json
        );
    }

    private Genome LoadChampionGenome(
        int genNumber)
    {
        string filePath =
            Path.Combine(
                saveFolderPath,
                "gen_" + genNumber + ".json"
            );

        if (File.Exists(filePath))
        {
            string json =
                File.ReadAllText(filePath);

            return JsonUtility.FromJson<Genome>(
                json
            );
        }

        return null;
    }

    private int GetLatestGenerationNumber()
    {
        if (!Directory.Exists(saveFolderPath))
            return 0;

        string[] files =
            Directory.GetFiles(
                saveFolderPath,
                "gen_*.json"
            );

        int maxGen = 0;

        foreach (string file in files)
        {
            string fileName =
                Path.GetFileNameWithoutExtension(file);

            string numberPart =
                fileName.Replace(
                    "gen_",
                    ""
                );

            if (int.TryParse(
                    numberPart,
                    out int gen))
            {
                if (gen > maxGen)
                {
                    maxGen = gen;
                }
            }
        }

        return maxGen;
    }

    private bool AllAgentsDead()
    {
        int count = 0;

        foreach (AgentController agent
                 in activeAgents)
        {
            if (agent != null &&
                agent.gameObject.activeSelf)
            {
                count++;
            }
        }

        return count == 0;
    }

    public int GetAliveCount()
    {
        int count = 0;

        foreach (AgentController agent
                 in activeAgents)
        {
            if (agent != null &&
                agent.gameObject.activeSelf)
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

        AgentController currentLeader = null;
        float maxFitness = -1f;

        for (int i = 0;
             i < activeAgents.Count;
             i++)
        {
            if (activeAgents[i] != null &&
                activeAgents[i].gameObject.activeSelf)
            {
                float agentFitness =
                    activeAgents[i].GetFitness();

                if (agentFitness > maxFitness)
                {
                    maxFitness = agentFitness;
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
                bool isLeader =
                    activeAgents[i] == currentLeader;

                activeAgents[i].SetHighlight(
                    isLeader
                );
            }
        }
    }
}