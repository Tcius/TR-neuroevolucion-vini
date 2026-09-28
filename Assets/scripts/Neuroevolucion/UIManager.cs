using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public EvolutionManager evolutionManager;
    public TextMeshProUGUI generationText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI bestFitnessText;
    public TextMeshProUGUI currentGenFitnessText;
    public TextMeshProUGUI hiddenNodesText;
    public TextMeshProUGUI aliveAgentsText;
    public TextMeshProUGUI completedAgentsText;
    public TextMeshProUGUI pelletsRemainingText;

    private void Update()
    {
        if (evolutionManager == null)
        {
            return;
        }

        UpdateUI();
    }

    private void UpdateUI()
    {
        if (timerText != null)
        {
            timerText.text =
                "Tiempo: " +
                evolutionManager.timer.ToString("F1") +
                "s";
        }

        if (generationText != null)
        {
            generationText.text =
                "Generacion: " +
                evolutionManager.generationCount;
        }

        if (bestFitnessText != null)
        {
            bestFitnessText.text =
                "Max Fitness Historico: " +
                evolutionManager
                    .GetHistoricalBestFitness()
                    .ToString("F1");
        }

        if (currentGenFitnessText != null)
        {
            currentGenFitnessText.text =
                "Max Fitness Actual: " +
                evolutionManager
                    .GetCurrentGenerationMaxFitness()
                    .ToString("F1");
        }

        if (hiddenNodesText != null)
        {
            int leaderHidden =
                CountHiddenNodesOfBestGenome();

            int maxHidden =
                CountMaximumHiddenNodesInPopulation();

            hiddenNodesText.text =
                "Neuronas Ocultas (Lider): " +
                leaderHidden +
                " | Max Poblacion: " +
                maxHidden;
        }

        if (aliveAgentsText != null)
        {
            aliveAgentsText.text =
                "Vivos: " +
                evolutionManager.GetAliveCount() +
                " / " +
                evolutionManager.populationSize;
        }

        if (completedAgentsText != null)
        {
            completedAgentsText.text =
                "Completados: " +
                evolutionManager.GetCurrentCompletedCount() +
                " / " +
                evolutionManager.populationSize;
        }

        if (pelletsRemainingText != null)
        {
            AgentController leader =
                GetBestAgent();

            string remaining =
                leader != null
                    ? leader.GetRemainingPellets().ToString()
                    : "-";

            pelletsRemainingText.text =
                "Bolitas Faltantes Lider: " +
                remaining;
        }
    }

    private AgentController GetBestAgent()
    {
        if (evolutionManager.activeAgents == null ||
            evolutionManager.activeAgents.Count == 0)
        {
            return null;
        }

        AgentController best = null;
        float bestFitness = float.MinValue;

        for (int i = 0;
             i < evolutionManager.activeAgents.Count;
             i++)
        {
            AgentController agent =
                evolutionManager.activeAgents[i];

            if (agent == null)
            {
                continue;
            }

            float fitness =
                agent.GetFitness();

            if (best == null ||
                fitness > bestFitness)
            {
                best = agent;
                bestFitness = fitness;
            }
        }

        return best;
    }

    private EvolutionManager.Genome GetBestGenome()
    {
        if (evolutionManager.population == null ||
            evolutionManager.population.Count == 0)
        {
            return null;
        }

        EvolutionManager.Genome best =
            evolutionManager.population[0];

        for (int i = 1;
             i < evolutionManager.population.Count;
             i++)
        {
            if (evolutionManager.population[i].fitness >
                best.fitness)
            {
                best =
                    evolutionManager.population[i];
            }
        }

        return best;
    }

    private int CountHiddenNodesOfBestGenome()
    {
        EvolutionManager.Genome bestGenome =
            GetBestGenome();

        if (bestGenome == null ||
            bestGenome.network == null ||
            bestGenome.network.nodes == null)
        {
            return 0;
        }

        int hidden = 0;

        for (int i = 0;
             i < bestGenome.network.nodes.Count;
             i++)
        {
            if (bestGenome.network.nodes[i] != null &&
                bestGenome.network.nodes[i].type ==
                EvolutionManager.NEATNode.NodeType.Hidden)
            {
                hidden++;
            }
        }

        return hidden;
    }

    private int CountMaximumHiddenNodesInPopulation()
    {
        if (evolutionManager.population == null ||
            evolutionManager.population.Count == 0)
        {
            return 0;
        }

        int maxHidden = 0;

        for (int i = 0;
             i < evolutionManager.population.Count;
             i++)
        {
            EvolutionManager.Genome genome =
                evolutionManager.population[i];

            if (genome == null ||
                genome.network == null ||
                genome.network.nodes == null)
            {
                continue;
            }

            int hidden = 0;

            for (int j = 0;
                 j < genome.network.nodes.Count;
                 j++)
            {
                if (genome.network.nodes[j] != null &&
                    genome.network.nodes[j].type ==
                    EvolutionManager.NEATNode.NodeType.Hidden)
                {
                    hidden++;
                }
            }

            if (hidden > maxHidden)
            {
                maxHidden = hidden;
            }
        }

        return maxHidden;
    }
}