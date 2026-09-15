using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class NeuralNetwork
{
    public List<NEATNode> nodes;
    public List<NEATConnection> connections;

    public NeuralNetwork()
    {
        nodes = new List<NEATNode>();
        connections = new List<NEATConnection>();
    }

    public float[] FeedForward(float[] inputValues, int outputCount)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return new float[outputCount];
        }

        List<NEATNode> inputNodes = nodes.FindAll(n => n.type == NEATNode.NodeType.Input);
        List<NEATNode> outputNodes = nodes.FindAll(n => n.type == NEATNode.NodeType.Output);
        List<NEATNode> hiddenNodes = nodes.FindAll(n => n.type == NEATNode.NodeType.Hidden);

        int inputsToAssign = Mathf.Min(inputValues.Length, inputNodes.Count);
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
                    if (conn != null && conn.enabled && conn.toNode != null && conn.toNode.id == hiddenNodes[i].id)
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
        for (int i = 0; i < outputNodes.Count && i < outputCount; i++)
        {
            float sum = 0f;
            if (connections != null)
            {
                foreach (NEATConnection conn in connections)
                {
                    if (conn != null && conn.enabled && conn.toNode != null && conn.toNode.id == outputNodes[i].id)
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

    private float Sigmoid(float x)
    {
        return 1f / (1f + Mathf.Exp(-x));
    }


    public bool ConnectionExists(NEATNode from, NEATNode to)
    {
        for (int i = 0; i < connections.Count; i++)
        {
            if (connections[i].fromNode.id == from.id && connections[i].toNode.id == to.id)
            {
                return true;
            }
        }
        return false;
    }
}