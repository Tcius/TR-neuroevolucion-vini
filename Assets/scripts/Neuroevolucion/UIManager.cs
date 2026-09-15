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

    private float bestFitnessEver = 0f;

    private void Update()
    {
        if (evolutionManager == null) return;

        UpdateUI();
    }

    private void UpdateUI()
    {
        if (timerText != null)
        {
            float tiempoRestante = Mathf.Max(0f, evolutionManager.generationDuration - evolutionManager.timer);
            timerText.text = "Tiempo: " + tiempoRestante.ToString("F1") + "s";
        }

        if (generationText != null)
        {
            generationText.text = "Generacion: " + evolutionManager.generationCount;
        }

        if (bestFitnessText != null)
        {
            float currentBest = GetCurrentBestFitness();
            if (currentBest > bestFitnessEver)
            {
                bestFitnessEver = currentBest;
            }
            bestFitnessText.text = "Max Fitness Historico: " + bestFitnessEver.ToString("F1");
        }

        if (currentGenFitnessText != null)
        {
            float genMax = evolutionManager.GetCurrentGenerationMaxFitness();
            currentGenFitnessText.text = "Max Fitness Actual: " + genMax.ToString("F1");
        }

        if (hiddenNodesText != null && evolutionManager.population != null && evolutionManager.population.Count > 0)
        {
            int hiddenCount = CountHiddenNodesOfBestGenome();
            hiddenNodesText.text = "Neuronas Ocultas (Lider): " + hiddenCount;
        }

        if (aliveAgentsText != null)
        {
            aliveAgentsText.text = "Vivos: " + evolutionManager.GetAliveCount() + " / " + evolutionManager.populationSize;
        }
    }

    private float GetCurrentBestFitness()
    {
        if (evolutionManager.population == null || evolutionManager.population.Count == 0) return 0f;

        float max = 0f;
        for (int i = 0; i < evolutionManager.population.Count; i++)
        {
            if (evolutionManager.population[i].fitness > max)
            {
                max = evolutionManager.population[i].fitness;
            }
        }
        return max;
    }

    private int CountHiddenNodesOfBestGenome()
    {
        if (evolutionManager.population == null || evolutionManager.population.Count == 0) return 0;

        EvolutionManager.Genome bestGenome = evolutionManager.population[0];
        float maxFitness = -1f;

        for (int i = 0; i < evolutionManager.population.Count; i++)
        {
            if (evolutionManager.population[i].fitness > maxFitness)
            {
                maxFitness = evolutionManager.population[i].fitness;
                bestGenome = evolutionManager.population[i];
            }
        }

        int hidden = 0;
        for (int i = 0; i < bestGenome.network.nodes.Count; i++)
        {
            if (bestGenome.network.nodes[i].type == EvolutionManager.NEATNode.NodeType.Hidden)
            {
                hidden++;
            }
        }
        return hidden;
    }
}