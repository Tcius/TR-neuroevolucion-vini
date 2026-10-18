using System;
using System.Collections.Generic;
using UnityEngine;

public static class ManualPacmanNetworkFactory
{
    private const int InputCount = 14;
    private const int OutputCount = 4;
    private const int FirstOutputNodeId = 14;

    [Serializable]
    public sealed class NetworkDefinition
    {
        public List<NodeDefinition> nodes = new List<NodeDefinition>();
        public List<ConnectionDefinition> connections = new List<ConnectionDefinition>();
    }

    [Serializable]
    public sealed class NodeDefinition
    {
        public int id;
        public string type;
        public float layer;
    }

    [Serializable]
    public sealed class ConnectionDefinition
    {
        public int fromNodeId;
        public int toNodeId;
        public float weight;
        public bool disabled;
    }

    public static EvolutionManager.Genome CreateGenome(TextAsset jsonAsset)
    {
        NetworkDefinition definition;
        bool usedDefaultDefinition = false;

        if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
        {
            Debug.LogError(
                "ManualPacmanNetworkFactory: falta asignar Manual Network Json. " +
                "Se usará la configuración predeterminada incorporada."
            );
            definition = CreateDefaultDefinition();
            usedDefaultDefinition = true;
        }
        else
        {
            try
            {
                definition = JsonUtility.FromJson<NetworkDefinition>(jsonAsset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "ManualPacmanNetworkFactory: no se pudo leer el JSON '" +
                    jsonAsset.name + "'. " + exception.Message +
                    " Se usará la configuración predeterminada incorporada."
                );
                definition = CreateDefaultDefinition();
                usedDefaultDefinition = true;
            }
        }

        if (!TryBuildNetwork(definition, out EvolutionManager.NeuralNetwork network, out string error))
        {
            Debug.LogError(
                "ManualPacmanNetworkFactory: configuración JSON no válida: " + error +
                " Se usará la configuración predeterminada incorporada."
            );

            usedDefaultDefinition = true;

            if (!TryBuildNetwork(CreateDefaultDefinition(), out network, out error))
            {
                Debug.LogError(
                    "ManualPacmanNetworkFactory: tampoco se pudo construir la red predeterminada. " + error
                );
                return null;
            }
        }

        network.manualNavigation = true;

        int hiddenNodeCount = network.nodes.FindAll(
            node => node.type == EvolutionManager.NEATNode.NodeType.Hidden
        ).Count;
        int enabledConnectionCount = network.connections.FindAll(
            connection => connection != null && connection.enabled
        ).Count;

        Debug.Log(
            "ManualPacmanNetworkFactory: " +
            (usedDefaultDefinition
                ? "se está usando la red predeterminada incorporada"
                : "JSON '" + jsonAsset.name + "' cargado correctamente") +
            ". Entradas=" + InputCount +
            ", ocultas=" + hiddenNodeCount +
            ", salidas=" + OutputCount +
            ", conexiones activas=" + enabledConnectionCount + "."
        );

        return new EvolutionManager.Genome(network);
    }

    private static bool TryBuildNetwork(
        NetworkDefinition definition,
        out EvolutionManager.NeuralNetwork network,
        out string error)
    {
        network = new EvolutionManager.NeuralNetwork();
        error = null;

        if (definition == null || definition.nodes == null || definition.connections == null)
        {
            error = "el JSON debe contener las listas 'nodes' y 'connections'.";
            return false;
        }

        Dictionary<int, EvolutionManager.NEATNode> nodesById =
            new Dictionary<int, EvolutionManager.NEATNode>();

        for (int i = 0; i < definition.nodes.Count; i++)
        {
            NodeDefinition source = definition.nodes[i];

            if (source == null)
            {
                error = "hay un nodo vacío en 'nodes'.";
                return false;
            }

            if (!Enum.TryParse(
                    source.type,
                    true,
                    out EvolutionManager.NEATNode.NodeType nodeType))
            {
                error = "tipo de nodo desconocido en el nodo " + source.id + ". Usa Input, Hidden u Output.";
                return false;
            }

            if (nodesById.ContainsKey(source.id))
            {
                error = "el ID de nodo " + source.id + " está duplicado.";
                return false;
            }

            EvolutionManager.NEATNode node =
                new EvolutionManager.NEATNode(
                    source.id,
                    nodeType,
                    source.layer
                );

            nodesById.Add(source.id, node);
            network.nodes.Add(node);
        }

        int inputNodeCount = 0;
        int outputNodeCount = 0;
        for (int i = 0; i < network.nodes.Count; i++)
        {
            if (network.nodes[i].type == EvolutionManager.NEATNode.NodeType.Input)
            {
                inputNodeCount++;
            }
            else if (network.nodes[i].type == EvolutionManager.NEATNode.NodeType.Output)
            {
                outputNodeCount++;
            }
        }

        if (inputNodeCount != InputCount)
        {
            error = "debe haber exactamente 14 nodos Input.";
            return false;
        }

        if (outputNodeCount != OutputCount)
        {
            error = "debe haber exactamente cuatro nodos Output.";
            return false;
        }

        for (int i = 0; i < InputCount; i++)
        {
            if (!nodesById.TryGetValue(i, out EvolutionManager.NEATNode inputNode) ||
                inputNode.type != EvolutionManager.NEATNode.NodeType.Input)
            {
                error = "deben existir los nodos de entrada Input con IDs del 0 al 13, en ese orden.";
                return false;
            }
        }

        for (int i = 0; i < OutputCount; i++)
        {
            int outputId = FirstOutputNodeId + i;
            if (!nodesById.TryGetValue(outputId, out EvolutionManager.NEATNode outputNode) ||
                outputNode.type != EvolutionManager.NEATNode.NodeType.Output)
            {
                error = "deben existir cuatro nodos Output con IDs del 14 al 17.";
                return false;
            }
        }

        int innovation = 1;
        for (int i = 0; i < definition.connections.Count; i++)
        {
            ConnectionDefinition source = definition.connections[i];

            if (source == null ||
                !nodesById.TryGetValue(source.fromNodeId, out EvolutionManager.NEATNode fromNode) ||
                !nodesById.TryGetValue(source.toNodeId, out EvolutionManager.NEATNode toNode))
            {
                error = "una conexión hace referencia a un nodo inexistente.";
                return false;
            }

            if (fromNode.type == EvolutionManager.NEATNode.NodeType.Output ||
                toNode.type == EvolutionManager.NEATNode.NodeType.Input)
            {
                error = "las conexiones deben ir hacia adelante: no pueden salir de Output ni entrar en Input.";
                return false;
            }

            if (fromNode.layer >= toNode.layer)
            {
                error = "la capa de origen debe ser menor que la de destino en cada conexión (" +
                        source.fromNodeId + " -> " + source.toNodeId + ").";
                return false;
            }

            if (float.IsNaN(source.weight) || float.IsInfinity(source.weight))
            {
                error = "una conexión tiene un peso no finito.";
                return false;
            }

            EvolutionManager.NEATConnection connection =
                new EvolutionManager.NEATConnection(
                    fromNode,
                    toNode,
                    source.weight,
                    innovation++
                );

            connection.enabled = !source.disabled;
            network.connections.Add(connection);
        }

        network.manualNavigation = true;
        return true;
    }

    private static NetworkDefinition CreateDefaultDefinition()
    {
        NetworkDefinition definition = new NetworkDefinition();

        for (int i = 0; i < InputCount; i++)
        {
            definition.nodes.Add(new NodeDefinition
            {
                id = i,
                type = "Input",
                layer = 0f
            });
        }

        for (int i = 0; i < OutputCount; i++)
        {
            definition.nodes.Add(new NodeDefinition
            {
                id = FirstOutputNodeId + i,
                type = "Output",
                layer = 10f
            });
        }

        // Primera capa oculta: preferencia de ruta y riesgo por acción.
        int[] targetInputIds = { 8, 9, 10, 11 };
        int[] riskInputIds = { 4, 6, 7, 5 };

        for (int i = 0; i < OutputCount; i++)
        {
            definition.nodes.Add(new NodeDefinition
            {
                id = 18 + i,
                type = "Hidden",
                layer = 1f
            });
            definition.nodes.Add(new NodeDefinition
            {
                id = 22 + i,
                type = "Hidden",
                layer = 1f
            });

            AddDefaultConnection(definition, targetInputIds[i], 18 + i, 2f);
            AddDefaultConnection(definition, riskInputIds[i], 22 + i, 2f);
        }

        // Segunda capa oculta: combina ir hacia la meta y evitar peligro.
        for (int i = 0; i < OutputCount; i++)
        {
            int utilityNodeId = 26 + i;
            definition.nodes.Add(new NodeDefinition
            {
                id = utilityNodeId,
                type = "Hidden",
                layer = 2f
            });

            AddDefaultConnection(definition, 18 + i, utilityNodeId, 3f);
            AddDefaultConnection(definition, 22 + i, utilityNodeId, -5f);
            AddDefaultConnection(definition, utilityNodeId, FirstOutputNodeId + i, 2f);
        }

        return definition;
    }

    private static void AddDefaultConnection(
        NetworkDefinition definition,
        int fromNodeId,
        int toNodeId,
        float weight)
    {
        definition.connections.Add(new ConnectionDefinition
        {
            fromNodeId = fromNodeId,
            toNodeId = toNodeId,
            weight = weight,
            disabled = false
        });
    }
}
