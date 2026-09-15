using UnityEngine;
using UnityEngine.UI;

public class OutputVisualizer : MonoBehaviour
{
    public Image[] outputImages = new Image[4];

    public Color activeColor = Color.green;
    public Color inactiveColor = Color.gray;

    public bool showOnlyWinningOutput = true;
    public float activeThreshold = 0.5f;

    public EvolutionManager evolutionManager;

    private void Update()
    {
        if (evolutionManager == null || outputImages == null || outputImages.Length < 4) return;

        AgentController leader = GetCurrentLeader();
        if (leader == null)
        {
            ResetAllImages();
            return;
        }

        float[] outputs = GetLeaderOutputs(leader);
        if (outputs == null || outputs.Length < 4)
        {
            ResetAllImages();
            return;
        }

        UpdateImageColors(outputs);
    }

    private AgentController GetCurrentLeader()
    {
        if (evolutionManager.activeAgents == null) return null;

        foreach (AgentController agent in evolutionManager.activeAgents)
        {
            if (agent != null && agent.gameObject.activeSelf)
            {
                SpriteRenderer sr = agent.GetComponent<SpriteRenderer>();
                if (sr == null) sr = agent.GetComponentInChildren<SpriteRenderer>();

                if (sr != null && sr.color == Color.darkRed)
                {
                    return agent;
                }
            }
        }
        return null;
    }

    private float[] GetLeaderOutputs(AgentController leader)
    {
        return leader.MyGenome != null && leader.MyGenome.network != null
            ? leader.MyGenome.network.FeedForward(leader.GetSensorInputsForVisualizer(), 4)
            : null;
    }

    private void UpdateImageColors(float[] outputs)
    {
        int winningIndex = -1;
        if (showOnlyWinningOutput)
        {
            float maxVal = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (outputs[i] > maxVal)
                {
                    maxVal = outputs[i];
                    winningIndex = i;
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (outputImages[i] == null) continue;

            bool isActive = showOnlyWinningOutput 
                ? (i == winningIndex) 
                : (outputs[i] >= activeThreshold);

            outputImages[i].color = isActive ? activeColor : inactiveColor;
        }
    }

    private void ResetAllImages()
    {
        for (int i = 0; i < outputImages.Length; i++)
        {
            if (outputImages[i] != null)
            {
                outputImages[i].color = inactiveColor;
            }
        }
    }
}