using System.Collections.Generic;
using UnityEngine;

public class PacmanSensors : MonoBehaviour
{
    public LayerMask wallLayer;
    public float maxRadarDistance = 10f;
    public bool debugSensors = false;
    public float pelletNodeAssociationDistance = 1.75f;
    public float pelletCorridorAssociationDistance = 0.65f;
    public float ghostNodeAssociationDistance = 2.0f;

    private const float DangerousGhostGraphRadius = 5f;
    private const int MaximumGhostChaseDistance = 12;
    private const float DangerousGhostBlockingDistance = 5f;

    private static readonly Vector2[] CardinalDirections =
    {
        Vector2.up,
        Vector2.down,
        Vector2.left,
        Vector2.right
    };

    private class NodeEdge
    {
        public Node node;
        public Vector2 direction;

        public NodeEdge(Node node, Vector2 direction)
        {
            this.node = node;
            this.direction = direction;
        }
    }

    private sealed class PelletAssociation
    {
        public Pellet pellet;
        public Node fromNode;
        public Node toNode;
        public Node nearestNode;
        public float projection;
        public float distanceToSegment;
        public float distanceToNode;

        public bool IsMapped
        {
            get { return (fromNode != null && toNode != null) || nearestNode != null; }
        }
    }

    private sealed class PelletRouteTarget
    {
        public Pellet pellet;
        public Node targetNode;
        public Vector2 firstDirection;
        public float routeCost;
        public int graphDistance;
    }

    private GameManager gameManager;
    private GameManager graphGameManager;
    private bool graphBuilt;

    private readonly List<Node> cachedNodes = new List<Node>();
    private Pellet[] cachedPellets;
    private readonly Dictionary<Vector2Int, Node> nodesByGridPosition =
        new Dictionary<Vector2Int, Node>();
    private readonly Dictionary<Node, List<NodeEdge>> nodeGraph =
        new Dictionary<Node, List<NodeEdge>>();
    private readonly List<PelletAssociation> pelletAssociations =
        new List<PelletAssociation>();
    private readonly Dictionary<Node, int> nodeVisitCounts =
        new Dictionary<Node, int>();
    private Node lastRecordedNode;
    private bool routeWarningLogged;

    private void Awake()
    {
        gameManager = GetComponentInParent<GameManager>();
        AutoConfigureLayers();
    }

    public float[] GetInputs(
        Transform closestPellet,
        Transform closestBigPellet)
    {
        AutoConfigureLayers();

        float radarDistance = Mathf.Max(0.01f, maxRadarDistance);
        float[] inputs = new float[14];

        inputs[0] = CheckWallDistance(Vector2.up, radarDistance);
        inputs[1] = CheckWallDistance(Vector2.down, radarDistance);
        inputs[2] = CheckWallDistance(Vector2.left, radarDistance);
        inputs[3] = CheckWallDistance(Vector2.right, radarDistance);

        Vector2 forward = GetCurrentDirection();
        Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 right = new Vector2(forward.y, -forward.x);

        Vector2[] relativeDirections =
        {
            forward,
            -forward,
            left,
            right
        };

        inputs[12] = GetVisibleGhostInputs(
            inputs,
            relativeDirections,
            radarDistance
        );

        if (closestPellet != null)
        {
            Vector2 directionToPellet = (
                closestPellet.position - transform.position
            ).normalized;

            inputs[8] = directionToPellet.x;
            inputs[9] = directionToPellet.y;
        }
        else
        {
            inputs[8] = 0f;
            inputs[9] = 0f;
        }

        if (closestBigPellet != null)
        {
            Vector2 directionToBigPellet = (
                closestBigPellet.position - transform.position
            ).normalized;

            inputs[10] = directionToBigPellet.x;
            inputs[11] = directionToBigPellet.y;
        }
        else
        {
            inputs[10] = 0f;
            inputs[11] = 0f;
        }

        float angle = Mathf.Atan2(forward.y, forward.x);
        inputs[13] = angle / Mathf.PI;

        if (debugSensors)
        {
            Debug.Log(
                $"[{name}] " +
                $"Fantasma delante: {inputs[4]:F2} | " +
                $"detrás: {inputs[5]:F2} | " +
                $"izquierda: {inputs[6]:F2} | " +
                $"derecha: {inputs[7]:F2} | " +
                $"Estado: {inputs[12]:F0} | " +
                $"Dirección: {inputs[13]:F2}",
                this
            );
        }

        return inputs;
    }

    public float[] GetManualInputs()
    {
        AutoConfigureLayers();

        float radarDistance = Mathf.Max(0.01f, maxRadarDistance);
        float[] inputs = new float[14];

        inputs[0] = CheckWallDistance(Vector2.up, radarDistance);
        inputs[1] = CheckWallDistance(Vector2.down, radarDistance);
        inputs[2] = CheckWallDistance(Vector2.left, radarDistance);
        inputs[3] = CheckWallDistance(Vector2.right, radarDistance);

        Vector2 forward = GetCurrentDirection();
        Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 right = new Vector2(forward.y, -forward.x);

        Vector2[] relativeDirections =
        {
            forward,
            -forward,
            left,
            right
        };

        Vector2[] actionDirections =
        {
            forward,
            left,
            right,
            -forward
        };

        inputs[12] = GetVisibleGhostInputs(
            inputs,
            relativeDirections,
            radarDistance
        );

        EnsureGraphBuilt();

        Node startNode = FindNearestNode(transform.position);
        Vector2 targetDirection = Vector2.zero;
        int nearestPelletDistance = int.MaxValue;
        Dictionary<Node, int> playerDistances = new Dictionary<Node, int>();
        Dictionary<Node, Vector2> firstDirections = new Dictionary<Node, Vector2>();
        Dictionary<Node, float> dangerousGhostDistances = BuildDangerousGhostDistances();
        int activePelletCount = CountActivePellets();
        int mappedPelletCount = 0;
        int reachablePelletCount = 0;

        if (startNode != null)
        {
            RecordNodeVisit(startNode);

            BuildPaths(
                startNode,
                out playerDistances,
                out firstDirections
            );

            Dictionary<Node, float> safeCosts;
            Dictionary<Node, Vector2> safeFirstDirections;
            BuildSafePaths(
                startNode,
                dangerousGhostDistances,
                out safeCosts,
                out safeFirstDirections
            );

            PelletRouteTarget pelletRoute = FindNearestPelletRoute(
                startNode,
                playerDistances,
                safeCosts,
                safeFirstDirections,
                dangerousGhostDistances,
                out activePelletCount,
                out mappedPelletCount,
                out reachablePelletCount
            );

            Pellet closestPellet = pelletRoute != null
                ? pelletRoute.pellet
                : null;

            if (pelletRoute != null)
            {
                targetDirection = pelletRoute.firstDirection;
                nearestPelletDistance = pelletRoute.graphDistance;
            }

            bool dangerousGhostClose = inputs[12] < 0f;
            if (dangerousGhostDistances.TryGetValue(startNode, out float ghostDistanceFromPlayer) &&
                ghostDistanceFromPlayer <= DangerousGhostBlockingDistance)
            {
                dangerousGhostClose = true;
            }

            if (!dangerousGhostClose)
            {
                Ghost edibleGhost;
                Node edibleGhostNode;
                int edibleGhostDistance;

                FindNearestEdibleGhostRoute(
                    playerDistances,
                    out edibleGhost,
                    out edibleGhostNode,
                    out edibleGhostDistance
                );

                if (edibleGhost != null &&
                    edibleGhostNode != null &&
                    edibleGhostDistance <= MaximumGhostChaseDistance)
                {
                    int detourAllowance = GetGhostDetourAllowance(
                        edibleGhost,
                        closestPellet
                    );

                    bool worthChasing =
                        nearestPelletDistance == int.MaxValue ||
                        edibleGhostDistance <= nearestPelletDistance + detourAllowance;

                    if (worthChasing &&
                        safeFirstDirections.TryGetValue(
                            edibleGhostNode,
                            out Vector2 edibleGhostDirection) &&
                        edibleGhostDirection != Vector2.zero)
                    {
                        targetDirection = edibleGhostDirection;
                    }
                }
            }

            if (targetDirection == Vector2.zero)
            {
                Node explorationTarget = FindExplorationTarget(
                    startNode,
                    playerDistances,
                    safeFirstDirections
                );

                if (explorationTarget != null &&
                    safeFirstDirections.TryGetValue(
                        explorationTarget,
                        out Vector2 explorationDirection))
                {
                    targetDirection = explorationDirection;
                }
            }

            if (activePelletCount > 0 && pelletRoute == null)
            {
                if (!routeWarningLogged)
                {
                    int edgeCount = 0;
                    foreach (KeyValuePair<Node, List<NodeEdge>> pair in nodeGraph)
                    {
                        edgeCount += pair.Value.Count;
                    }

                    Debug.LogWarning(
                        "PacmanSensors: no se ha encontrado una ruta a una bolita activa. " +
                        "Nodos=" + cachedNodes.Count +
                        "; conexiones dirigidas=" + edgeCount +
                        "; bolitas activas=" + activePelletCount +
                        "; asociadas a nodo/pasillo=" + mappedPelletCount +
                        "; asociadas alcanzables=" + reachablePelletCount +
                        "; tolerancia nodo=" + pelletNodeAssociationDistance.ToString("F2") +
                        "; tolerancia pasillo=" + pelletCorridorAssociationDistance.ToString("F2") +
                        ". Si las bolitas asociadas son alcanzables=0, revisa el grafo; si asociadas=0, revisa coordenadas y tolerancias.",
                        this
                    );
                    routeWarningLogged = true;
                }
            }
            else if (pelletRoute != null)
            {
                routeWarningLogged = false;
            }

            AddGraphGhostRisk(
                inputs,
                startNode,
                relativeDirections,
                dangerousGhostDistances
            );
        }

        if (targetDirection == Vector2.zero)
        {
            targetDirection = GetFallbackDirection(startNode, forward);
        }

        EncodeTargetDirection(
            inputs,
            targetDirection,
            actionDirections
        );

        float angle = Mathf.Atan2(forward.y, forward.x);
        inputs[13] = angle / Mathf.PI;

        if (debugSensors)
        {
            Debug.Log(
                $"[{name}] Manual | " +
                $"Ruta F/I/D/R: {inputs[8]:F0}/{inputs[9]:F0}/{inputs[10]:F0}/{inputs[11]:F0} | " +
                $"Peligro F/R/I/D: {inputs[4]:F2}/{inputs[5]:F2}/{inputs[6]:F2}/{inputs[7]:F2} | " +
                $"Estado: {inputs[12]:F0} | " +
                $"Bolitas activas/asociadas/alcanzables: {activePelletCount}/{mappedPelletCount}/{reachablePelletCount}",
                this
            );
        }

        return inputs;
    }

    private float GetVisibleGhostInputs(
        float[] inputs,
        Vector2[] relativeDirections,
        float radarDistance)
    {
        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (gameManager == null ||
            gameManager.ghosts == null ||
            gameManager.ghosts.Length == 0)
        {
            return 0f;
        }

        Vector2 origin = transform.position;
        bool foundVisibleDangerousGhost = false;
        bool foundVisibleFrightenedGhost = false;

        for (int i = 0; i < gameManager.ghosts.Length; i++)
        {
            Ghost ghost = gameManager.ghosts[i];

            if (ghost == null || !ghost.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (ghost.home != null && ghost.home.enabled)
            {
                continue;
            }

            if (ghost.GetComponentInParent<GameManager>() != gameManager)
            {
                continue;
            }

            Vector2 offset = (Vector2)ghost.transform.position - origin;
            float distance = offset.magnitude;

            if (distance > radarDistance || distance <= 0.001f)
            {
                continue;
            }

            Vector2 directionToGhost = offset / distance;

            if (HasWallBetween(origin, directionToGhost, distance))
            {
                continue;
            }

            bool frightened =
                ghost.frightened != null && ghost.frightened.enabled;

            if (frightened)
            {
                foundVisibleFrightenedGhost = true;
                continue;
            }

            foundVisibleDangerousGhost = true;

            float proximity = Mathf.Clamp01(1f - distance / radarDistance);

            for (int directionIndex = 0;
                 directionIndex < relativeDirections.Length;
                 directionIndex++)
            {
                float alignment = Vector2.Dot(
                    directionToGhost,
                    relativeDirections[directionIndex]
                );

                if (alignment <= 0f)
                {
                    continue;
                }

                float signal = proximity * Mathf.Clamp01(alignment);
                int inputIndex = 4 + directionIndex;
                inputs[inputIndex] = Mathf.Max(inputs[inputIndex], signal);
            }
        }

        if (foundVisibleDangerousGhost)
        {
            return -1f;
        }

        if (foundVisibleFrightenedGhost)
        {
            return 1f;
        }

        return 0f;
    }

    private void EnsureGraphBuilt()
    {
        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (graphBuilt &&
            graphGameManager == gameManager &&
            cachedNodes.Count > 0)
        {
            return;
        }

        graphBuilt = false;
        graphGameManager = gameManager;
        cachedNodes.Clear();
        cachedPellets = null;
        pelletAssociations.Clear();
        nodesByGridPosition.Clear();
        nodeGraph.Clear();
        nodeVisitCounts.Clear();
        lastRecordedNode = null;
        routeWarningLogged = false;

        if (gameManager == null)
        {
            return;
        }

        Node[] foundNodes =
            gameManager.GetComponentsInChildren<Node>(true);

        cachedPellets = gameManager.pellets != null
            ? gameManager.pellets.GetComponentsInChildren<Pellet>(true)
            : new Pellet[0];

        for (int i = 0; i < foundNodes.Length; i++)
        {
            Node node = foundNodes[i];

            if (node == null || !node.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (node.GetComponentInParent<GameManager>() != gameManager)
            {
                continue;
            }

            cachedNodes.Add(node);

            Vector2Int gridPosition = GetGridPosition(node.transform.position);
            if (!nodesByGridPosition.ContainsKey(gridPosition))
            {
                nodesByGridPosition.Add(gridPosition, node);
            }
        }

        // Conecta cada nodo con el siguiente nodo visible en cada dirección cardinal.
        // No presupone que el siguiente nodo esté exactamente a una unidad de distancia.
        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node node = cachedNodes[i];
            List<NodeEdge> edges = new List<NodeEdge>();

            for (int d = 0; d < CardinalDirections.Length; d++)
            {
                Vector2 direction = CardinalDirections[d];
                Node neighbour = FindNearestNodeInDirection(node, direction);
                if (neighbour != null && neighbour != node)
                {
                    edges.Add(new NodeEdge(neighbour, direction));
                }
            }

            nodeGraph[node] = edges;
        }

        // La navegación física de Pac-Man es bidireccional. Añade la arista inversa
        // cuando el tramo está libre, incluso si availableDirections perdió esa dirección.
        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node node = cachedNodes[i];
            if (node == null || !nodeGraph.TryGetValue(node, out List<NodeEdge> edges))
            {
                continue;
            }

            for (int j = 0; j < edges.Count; j++)
            {
                NodeEdge edge = edges[j];
                if (edge == null || edge.node == null || HasGraphEdge(edge.node, node))
                {
                    continue;
                }

                float distance = Vector2.Distance(node.transform.position, edge.node.transform.position);
                Vector2 reverseDirection = -edge.direction;
                if (!HasObstacleBetweenNodes(edge.node, reverseDirection, distance) &&
                    nodeGraph.TryGetValue(edge.node, out List<NodeEdge> reverseEdges))
                {
                    reverseEdges.Add(new NodeEdge(node, reverseDirection));
                }
            }
        }

        // El controlador de movimiento también consulta availableDirections. Añade las
        // direcciones de arista verificadas para que planificador y movimiento coincidan.
        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node node = cachedNodes[i];
            if (node == null || !nodeGraph.TryGetValue(node, out List<NodeEdge> edges) ||
                node.availableDirections == null)
            {
                continue;
            }

            for (int j = 0; j < edges.Count; j++)
            {
                Vector2 direction = edges[j].direction;
                bool exists = false;
                for (int k = 0; k < node.availableDirections.Count; k++)
                {
                    if (Vector2.Dot(node.availableDirections[k], direction) > 0.99f)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    node.availableDirections.Add(direction);
                }
            }
        }

        graphBuilt = cachedNodes.Count > 0;

        BuildPelletAssociations();

        if (debugSensors)
        {
            int edgeCount = 0;
            foreach (KeyValuePair<Node, List<NodeEdge>> pair in nodeGraph)
            {
                edgeCount += pair.Value.Count;
            }

            int mappedCount = 0;
            for (int i = 0; i < pelletAssociations.Count; i++)
            {
                if (pelletAssociations[i] != null && pelletAssociations[i].IsMapped)
                {
                    mappedCount++;
                }
            }

            Debug.Log(
                "PacmanSensors: grafo construido. Nodos=" + cachedNodes.Count +
                ", conexiones dirigidas=" + edgeCount +
                ", bolitas detectadas=" + (cachedPellets != null ? cachedPellets.Length : 0) +
                ", bolitas asociadas a nodo/pasillo=" + mappedCount + ".",
                this
            );
        }
    }

    private void BuildPelletAssociations()
    {
        pelletAssociations.Clear();

        if (cachedPellets == null)
        {
            return;
        }

        float segmentTolerance = Mathf.Max(0.05f, pelletCorridorAssociationDistance);
        float nodeTolerance = Mathf.Max(segmentTolerance, pelletNodeAssociationDistance);

        for (int i = 0; i < cachedPellets.Length; i++)
        {
            Pellet pellet = cachedPellets[i];
            if (pellet == null)
            {
                pelletAssociations.Add(null);
                continue;
            }

            Vector2 pelletPosition = pellet.transform.position;
            PelletAssociation bestAssociation = null;
            float bestSegmentDistance = float.PositiveInfinity;

            foreach (KeyValuePair<Node, List<NodeEdge>> pair in nodeGraph)
            {
                Node fromNode = pair.Key;
                if (fromNode == null || pair.Value == null)
                {
                    continue;
                }

                Vector2 a = fromNode.transform.position;
                for (int edgeIndex = 0; edgeIndex < pair.Value.Count; edgeIndex++)
                {
                    NodeEdge edge = pair.Value[edgeIndex];
                    if (edge == null || edge.node == null)
                    {
                        continue;
                    }

                    Vector2 b = edge.node.transform.position;
                    Vector2 segment = b - a;
                    float lengthSquared = segment.sqrMagnitude;
                    if (lengthSquared <= 0.0001f)
                    {
                        continue;
                    }

                    float projection = Mathf.Clamp01(
                        Vector2.Dot(pelletPosition - a, segment) / lengthSquared
                    );
                    Vector2 closestPoint = a + segment * projection;
                    float distance = Vector2.Distance(pelletPosition, closestPoint);

                    if (distance <= segmentTolerance && distance < bestSegmentDistance)
                    {
                        bestSegmentDistance = distance;
                        bestAssociation = new PelletAssociation
                        {
                            pellet = pellet,
                            fromNode = fromNode,
                            toNode = edge.node,
                            projection = projection,
                            distanceToSegment = distance
                        };
                    }
                }
            }

            if (bestAssociation == null)
            {
                Node nearestNode = FindNearestVisibleNode(
                    pellet.transform.position,
                    nodeTolerance
                );

                if (nearestNode != null)
                {
                    bestAssociation = new PelletAssociation
                    {
                        pellet = pellet,
                        nearestNode = nearestNode,
                        distanceToNode = Vector2.Distance(
                            pellet.transform.position,
                            nearestNode.transform.position
                        )
                    };
                }
            }

            pelletAssociations.Add(bestAssociation);
        }
    }

    private Node FindNearestVisibleNode(Vector3 worldPosition, float maxDistance)
    {
        Node nearestNode = null;
        float nearestDistanceSquared = maxDistance * maxDistance;
        Vector2 position = worldPosition;

        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node node = cachedNodes[i];
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector2 delta = (Vector2)node.transform.position - position;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            if (distance > 0.05f && HasWallBetween(position, delta / distance, distance))
            {
                continue;
            }

            nearestDistanceSquared = distanceSquared;
            nearestNode = node;
        }

        return nearestNode;
    }

    private Node FindNearestNodeOnGraph(Vector3 worldPosition, float maxNodeDistance)
    {
        Node node = FindNearestVisibleNode(worldPosition, maxNodeDistance);
        if (node != null)
        {
            return node;
        }

        Vector2 position = worldPosition;
        Node bestEndpoint = null;
        float bestDistance = Mathf.Max(0.05f, pelletCorridorAssociationDistance);

        foreach (KeyValuePair<Node, List<NodeEdge>> pair in nodeGraph)
        {
            Node fromNode = pair.Key;
            if (fromNode == null || pair.Value == null)
            {
                continue;
            }

            Vector2 a = fromNode.transform.position;
            for (int i = 0; i < pair.Value.Count; i++)
            {
                NodeEdge edge = pair.Value[i];
                if (edge == null || edge.node == null)
                {
                    continue;
                }

                Vector2 b = edge.node.transform.position;
                Vector2 segment = b - a;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.0001f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(Vector2.Dot(position - a, segment) / lengthSquared);
                float distance = Vector2.Distance(position, a + segment * t);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    bestEndpoint = t <= 0.5f ? fromNode : edge.node;
                }
            }
        }

        return bestEndpoint;
    }

    private Node FindNearestNodeInDirection(Node source, Vector2 direction)
    {
        if (source == null)
        {
            return null;
        }

        Node bestNode = null;
        float bestProjection = float.PositiveInfinity;
        Vector2 sourcePosition = source.transform.position;

        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node candidate = cachedNodes[i];
            if (candidate == null || candidate == source)
            {
                continue;
            }

            Vector2 delta = (Vector2)candidate.transform.position - sourcePosition;
            float projection = Vector2.Dot(delta, direction);
            if (projection <= 0.25f || projection >= bestProjection)
            {
                continue;
            }

            float perpendicularDistance = Mathf.Abs(
                delta.x * direction.y - delta.y * direction.x
            );

            if (perpendicularDistance > 0.2f)
            {
                continue;
            }

            if (HasObstacleBetweenNodes(source, direction, projection))
            {
                continue;
            }

            bestProjection = projection;
            bestNode = candidate;
        }

        return bestNode;
    }

    private bool HasObstacleBetweenNodes(Node source, Vector2 direction, float distance)
    {
        LayerMask mask = 0;
        if (source != null)
        {
            mask = source.obstacleLayer;
        }

        if (mask.value == 0)
        {
            mask = wallLayer;
        }

        if (mask.value == 0 || distance <= 0.55f)
        {
            return false;
        }

        float castDistance = Mathf.Max(0.01f, distance - 0.5f);
        RaycastHit2D hit = Physics2D.BoxCast(
            source.transform.position,
            Vector2.one * 0.4f,
            0f,
            direction,
            castDistance,
            mask
        );

        return hit.collider != null;
    }

    private List<Vector2> GetNodeDirections(Node node)
    {
        List<Vector2> directions = new List<Vector2>();

        if (node.availableDirections != null &&
            node.availableDirections.Count > 0)
        {
            directions.AddRange(node.availableDirections);
            return directions;
        }

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (NodeAllowsDirection(node, CardinalDirections[i]))
            {
                directions.Add(CardinalDirections[i]);
            }
        }

        return directions;
    }

    private bool NodeAllowsDirection(Node node, Vector2 direction)
    {
        if (node == null)
        {
            return false;
        }

        if (node.availableDirections != null && node.availableDirections.Count > 0)
        {
            for (int i = 0; i < node.availableDirections.Count; i++)
            {
                if (Vector2.Dot(node.availableDirections[i], direction) > 0.99f)
                {
                    return true;
                }
            }

            return false;
        }

        LayerMask mask = node.obstacleLayer;
        if (mask.value == 0)
        {
            mask = wallLayer;
        }

        if (mask.value == 0)
        {
            return true;
        }

        RaycastHit2D hit = Physics2D.BoxCast(
            node.transform.position,
            Vector2.one * 0.5f,
            0f,
            direction,
            1f,
            mask
        );

        return hit.collider == null;
    }

    private void BuildPaths(
        Node startNode,
        out Dictionary<Node, int> distances,
        out Dictionary<Node, Vector2> firstDirections)
    {
        distances = new Dictionary<Node, int>();
        firstDirections = new Dictionary<Node, Vector2>();
        Queue<Node> queue = new Queue<Node>();

        distances[startNode] = 0;
        firstDirections[startNode] = Vector2.zero;
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            Node current = queue.Dequeue();

            if (!nodeGraph.TryGetValue(current, out List<NodeEdge> edges))
            {
                continue;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                NodeEdge edge = edges[i];

                if (distances.ContainsKey(edge.node))
                {
                    continue;
                }

                distances[edge.node] = distances[current] + 1;
                firstDirections[edge.node] = current == startNode
                    ? edge.direction
                    : firstDirections[current];

                queue.Enqueue(edge.node);
            }
        }
    }

    private void BuildSafePaths(
        Node startNode,
        Dictionary<Node, float> dangerousGhostDistances,
        out Dictionary<Node, float> costs,
        out Dictionary<Node, Vector2> firstDirections)
    {
        costs = new Dictionary<Node, float>();
        firstDirections = new Dictionary<Node, Vector2>();
        HashSet<Node> visited = new HashSet<Node>();

        if (startNode == null)
        {
            return;
        }

        costs[startNode] = 0f;
        firstDirections[startNode] = Vector2.zero;

        for (int iteration = 0; iteration < cachedNodes.Count; iteration++)
        {
            Node current = null;
            float currentCost = float.PositiveInfinity;

            foreach (KeyValuePair<Node, float> pair in costs)
            {
                if (!visited.Contains(pair.Key) && pair.Value < currentCost)
                {
                    current = pair.Key;
                    currentCost = pair.Value;
                }
            }

            if (current == null)
            {
                break;
            }

            visited.Add(current);

            if (!nodeGraph.TryGetValue(current, out List<NodeEdge> edges))
            {
                continue;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                NodeEdge edge = edges[i];
                if (edge == null || edge.node == null || visited.Contains(edge.node))
                {
                    continue;
                }

                float segmentLength = Vector2.Distance(
                    current.transform.position,
                    edge.node.transform.position
                );
                segmentLength = Mathf.Max(0.1f, segmentLength);

                float nextCost = currentCost + segmentLength +
                    GetGhostPathPenalty(edge.node, dangerousGhostDistances);

                if (!costs.TryGetValue(edge.node, out float oldCost) || nextCost < oldCost)
                {
                    costs[edge.node] = nextCost;
                    firstDirections[edge.node] = current == startNode
                        ? edge.direction
                        : firstDirections[current];
                }
            }
        }
    }

    private float GetGhostPathPenalty(
        Node node,
        Dictionary<Node, float> dangerousGhostDistances)
    {
        if (node == null || dangerousGhostDistances == null ||
            !dangerousGhostDistances.TryGetValue(node, out float distance))
        {
            return 0f;
        }

        if (distance <= 0.75f) return 35f;
        if (distance <= 1.5f) return 18f;
        if (distance <= 2.5f) return 7f;
        if (distance <= 3.5f) return 2.5f;
        if (distance <= 5f) return 0.75f;
        return 0f;
    }

    private Dictionary<Node, float> BuildDangerousGhostDistances()
    {
        Dictionary<Node, float> nearestDistances = new Dictionary<Node, float>();

        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (gameManager == null || gameManager.ghosts == null)
        {
            return nearestDistances;
        }

        for (int i = 0; i < gameManager.ghosts.Length; i++)
        {
            Ghost ghost = gameManager.ghosts[i];
            if (ghost == null || !ghost.gameObject.activeInHierarchy ||
                ghost.GetComponentInParent<GameManager>() != gameManager ||
                (ghost.home != null && ghost.home.enabled) ||
                (ghost.frightened != null && ghost.frightened.enabled))
            {
                continue;
            }

            Node ghostNode = FindNearestNodeOnGraph(
                ghost.transform.position,
                Mathf.Max(0.1f, ghostNodeAssociationDistance)
            );

            if (ghostNode == null)
            {
                continue;
            }

            Dictionary<Node, float> distances = BuildWeightedDistancesFrom(ghostNode);
            foreach (KeyValuePair<Node, float> pair in distances)
            {
                if (!nearestDistances.TryGetValue(pair.Key, out float oldDistance) ||
                    pair.Value < oldDistance)
                {
                    nearestDistances[pair.Key] = pair.Value;
                }
            }
        }

        return nearestDistances;
    }

    private Dictionary<Node, float> BuildWeightedDistancesFrom(Node startNode)
    {
        Dictionary<Node, float> distances = new Dictionary<Node, float>();
        HashSet<Node> visited = new HashSet<Node>();

        if (startNode == null)
        {
            return distances;
        }

        distances[startNode] = 0f;

        for (int iteration = 0; iteration < cachedNodes.Count; iteration++)
        {
            Node current = null;
            float currentDistance = float.PositiveInfinity;

            foreach (KeyValuePair<Node, float> pair in distances)
            {
                if (!visited.Contains(pair.Key) && pair.Value < currentDistance)
                {
                    current = pair.Key;
                    currentDistance = pair.Value;
                }
            }

            if (current == null)
            {
                break;
            }

            visited.Add(current);
            if (!nodeGraph.TryGetValue(current, out List<NodeEdge> edges))
            {
                continue;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                NodeEdge edge = edges[i];
                if (edge == null || edge.node == null || visited.Contains(edge.node))
                {
                    continue;
                }

                float length = Mathf.Max(0.1f, Vector2.Distance(
                    current.transform.position,
                    edge.node.transform.position
                ));
                float nextDistance = currentDistance + length;

                if (!distances.TryGetValue(edge.node, out float oldDistance) ||
                    nextDistance < oldDistance)
                {
                    distances[edge.node] = nextDistance;
                }
            }
        }

        return distances;
    }

    private Dictionary<Node, int> BuildDistancesFrom(Node startNode)
    {
        Dictionary<Node, int> distances = new Dictionary<Node, int>();
        Queue<Node> queue = new Queue<Node>();

        distances[startNode] = 0;
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            Node current = queue.Dequeue();

            if (!nodeGraph.TryGetValue(current, out List<NodeEdge> edges))
            {
                continue;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                Node neighbour = edges[i].node;

                if (distances.ContainsKey(neighbour))
                {
                    continue;
                }

                distances[neighbour] = distances[current] + 1;
                queue.Enqueue(neighbour);
            }
        }

        return distances;
    }

    private PelletRouteTarget FindNearestPelletRoute(
        Node startNode,
        Dictionary<Node, int> playerDistances,
        Dictionary<Node, float> safeCosts,
        Dictionary<Node, Vector2> safeFirstDirections,
        Dictionary<Node, float> dangerousGhostDistances,
        out int activeCount,
        out int mappedCount,
        out int reachableCount)
    {
        activeCount = 0;
        mappedCount = 0;
        reachableCount = 0;

        PelletRouteTarget bestTarget = null;
        float bestCost = float.PositiveInfinity;

        for (int i = 0; i < pelletAssociations.Count; i++)
        {
            PelletAssociation association = pelletAssociations[i];
            if (association == null || association.pellet == null ||
                !association.pellet.gameObject.activeInHierarchy)
            {
                continue;
            }

            activeCount++;
            if (!association.IsMapped)
            {
                continue;
            }

            mappedCount++;
            bool pelletReachable = false;

            if (association.fromNode != null && association.toNode != null)
            {
                Node fromNode = association.fromNode;
                Node toNode = association.toNode;
                float t = association.projection;
                float segmentLength = Mathf.Max(0.1f, Vector2.Distance(
                    fromNode.transform.position,
                    toNode.transform.position
                ));
                Vector2 edgeDirection = (toNode.transform.position - fromNode.transform.position).normalized;

                if (safeCosts.TryGetValue(fromNode, out float fromCost) &&
                    safeFirstDirections.TryGetValue(fromNode, out Vector2 fromFirstDirection))
                {
                    pelletReachable = true;
                    Vector2 routeDirection = fromNode == startNode
                        ? edgeDirection
                        : fromFirstDirection;
                    // The route to the segment endpoint already includes risk penalties.
                    // A pellet inside the segment receives only the proportional part of that risk.
                    float candidateCost = fromCost + segmentLength * t +
                        GetGhostPathPenalty(toNode, dangerousGhostDistances) * t;

                    int graphDistance = playerDistances.TryGetValue(fromNode, out int fromSteps)
                        ? fromSteps + (t > 0.05f ? 1 : 0)
                        : int.MaxValue;

                    ConsiderPelletRoute(
                        association.pellet, fromNode, routeDirection, candidateCost,
                        graphDistance, ref bestTarget, ref bestCost
                    );
                }

                if (HasGraphEdge(toNode, fromNode) &&
                    safeCosts.TryGetValue(toNode, out float toCost) &&
                    safeFirstDirections.TryGetValue(toNode, out Vector2 toFirstDirection))
                {
                    pelletReachable = true;
                    Vector2 reverseDirection = -edgeDirection;
                    Vector2 routeDirection = toNode == startNode
                        ? reverseDirection
                        : toFirstDirection;
                    float remainingFraction = 1f - t;
                    float candidateCost = toCost + segmentLength * remainingFraction +
                        GetGhostPathPenalty(fromNode, dangerousGhostDistances) * remainingFraction;

                    int graphDistance = playerDistances.TryGetValue(toNode, out int toSteps)
                        ? toSteps + (remainingFraction > 0.05f ? 1 : 0)
                        : int.MaxValue;

                    ConsiderPelletRoute(
                        association.pellet, toNode, routeDirection, candidateCost,
                        graphDistance, ref bestTarget, ref bestCost
                    );
                }
            }
            else if (association.nearestNode != null &&
                     safeCosts.TryGetValue(association.nearestNode, out float nodeCost) &&
                     safeFirstDirections.TryGetValue(association.nearestNode, out Vector2 nodeFirstDirection))
            {
                Node targetNode = association.nearestNode;
                Vector2 routeDirection = targetNode == startNode
                    ? GetDirectionTowardPelletFromNode(startNode, association.pellet.transform.position)
                    : nodeFirstDirection;
                float candidateCost = nodeCost + association.distanceToNode;
                int graphDistance = playerDistances.TryGetValue(targetNode, out int nodeSteps)
                    ? nodeSteps
                    : int.MaxValue;

                if (routeDirection != Vector2.zero)
                {
                    pelletReachable = true;
                    ConsiderPelletRoute(
                        association.pellet, targetNode, routeDirection, candidateCost,
                        graphDistance, ref bestTarget, ref bestCost
                    );
                }
            }

            if (pelletReachable)
            {
                reachableCount++;
            }
        }

        return bestTarget;
    }

    private void ConsiderPelletRoute(
        Pellet pellet,
        Node targetNode,
        Vector2 routeDirection,
        float routeCost,
        int graphDistance,
        ref PelletRouteTarget bestTarget,
        ref float bestCost)
    {
        if (pellet == null || routeDirection == Vector2.zero || float.IsNaN(routeCost) ||
            float.IsInfinity(routeCost) || routeCost >= bestCost)
        {
            return;
        }

        bestCost = routeCost;
        bestTarget = new PelletRouteTarget
        {
            pellet = pellet,
            targetNode = targetNode,
            firstDirection = routeDirection,
            routeCost = routeCost,
            graphDistance = graphDistance
        };
    }

    private bool HasGraphEdge(Node fromNode, Node toNode)
    {
        if (fromNode == null || toNode == null ||
            !nodeGraph.TryGetValue(fromNode, out List<NodeEdge> edges))
        {
            return false;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i] != null && edges[i].node == toNode)
            {
                return true;
            }
        }

        return false;
    }

    private Vector2 GetDirectionTowardPelletFromNode(Node node, Vector3 pelletPosition)
    {
        if (node == null || !nodeGraph.TryGetValue(node, out List<NodeEdge> edges))
        {
            return Vector2.zero;
        }

        Vector2 delta = (Vector2)pelletPosition - (Vector2)node.transform.position;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return Vector2.zero;
        }

        Vector2 preferredDirection = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2(Mathf.Sign(delta.x), 0f)
            : new Vector2(0f, Mathf.Sign(delta.y));

        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i] != null && edges[i].direction == preferredDirection)
            {
                return preferredDirection;
            }
        }

        return Vector2.zero;
    }

    private void FindNearestEdibleGhostRoute(
        Dictionary<Node, int> playerDistances,
        out Ghost bestGhost,
        out Node bestNode,
        out int bestDistance)
    {
        bestGhost = null;
        bestNode = null;
        bestDistance = int.MaxValue;

        if (gameManager == null || gameManager.ghosts == null)
        {
            return;
        }

        for (int i = 0; i < gameManager.ghosts.Length; i++)
        {
            Ghost ghost = gameManager.ghosts[i];

            if (ghost == null || !ghost.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (ghost.GetComponentInParent<GameManager>() != gameManager ||
                (ghost.home != null && ghost.home.enabled) ||
                ghost.frightened == null ||
                !ghost.frightened.enabled)
            {
                continue;
            }

            Node ghostNode = FindNearestNodeOnGraph(ghost.transform.position, ghostNodeAssociationDistance);

            if (ghostNode == null ||
                !playerDistances.TryGetValue(ghostNode, out int pathDistance))
            {
                continue;
            }

            if (pathDistance < bestDistance)
            {
                bestDistance = pathDistance;
                bestGhost = ghost;
                bestNode = ghostNode;
            }
        }
    }

    private int GetGhostDetourAllowance(Ghost ghost, Pellet closestPellet)
    {
        float ghostReward = Mathf.Max(1, ghost.points) *
            Mathf.Max(1, gameManager != null ? gameManager.ghostMultiplier : 1);

        float pelletReward = closestPellet != null
            ? Mathf.Max(1, closestPellet.points)
            : 10f;

        float rewardRatio = ghostReward / pelletReward;
        return Mathf.Clamp(
            Mathf.RoundToInt(1.4f * Mathf.Sqrt(rewardRatio)),
            3,
            8
        );
    }

    private int GetNearestDangerousGhostDistance(
        Dictionary<Node, int> playerDistances)
    {
        if (gameManager == null || gameManager.ghosts == null)
        {
            return int.MaxValue;
        }

        int nearestDistance = int.MaxValue;

        for (int i = 0; i < gameManager.ghosts.Length; i++)
        {
            Ghost ghost = gameManager.ghosts[i];

            if (ghost == null || !ghost.gameObject.activeInHierarchy ||
                ghost.GetComponentInParent<GameManager>() != gameManager ||
                (ghost.home != null && ghost.home.enabled) ||
                (ghost.frightened != null && ghost.frightened.enabled))
            {
                continue;
            }

            Node ghostNode = FindNearestNodeOnGraph(ghost.transform.position, ghostNodeAssociationDistance);

            if (ghostNode != null &&
                playerDistances.TryGetValue(ghostNode, out int distance) &&
                distance < nearestDistance)
            {
                nearestDistance = distance;
            }
        }

        return nearestDistance;
    }

    private void AddGraphGhostRisk(
        float[] inputs,
        Node startNode,
        Vector2[] relativeDirections,
        Dictionary<Node, float> dangerousGhostDistances)
    {
        if (startNode == null || dangerousGhostDistances == null)
        {
            return;
        }

        for (int directionIndex = 0;
             directionIndex < relativeDirections.Length;
             directionIndex++)
        {
            Node nextNode = FindNeighbour(startNode, relativeDirections[directionIndex]);
            if (nextNode == null ||
                !dangerousGhostDistances.TryGetValue(nextNode, out float distance))
            {
                continue;
            }

            float graphRisk = Mathf.Clamp01(
                1f - distance / Mathf.Max(0.1f, DangerousGhostGraphRadius)
            );

            int inputIndex = 4 + directionIndex;
            inputs[inputIndex] = Mathf.Max(inputs[inputIndex], graphRisk);
        }
    }

    private Node FindNeighbour(Node node, Vector2 direction)
    {
        if (node == null || !nodeGraph.TryGetValue(node, out List<NodeEdge> edges))
        {
            return null;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].direction == direction)
            {
                return edges[i].node;
            }
        }

        return null;
    }

    private Vector2 GetFallbackDirection(Node startNode, Vector2 forward)
    {
        Node forwardNeighbour = FindNeighbour(startNode, forward);

        if (forwardNeighbour != null)
        {
            return forward;
        }

        if (startNode != null && nodeGraph.TryGetValue(startNode, out List<NodeEdge> edges))
        {
            for (int i = 0; i < edges.Count; i++)
            {
                return edges[i].direction;
            }
        }

        return Vector2.zero;
    }

    private void EncodeTargetDirection(
        float[] inputs,
        Vector2 targetDirection,
        Vector2[] relativeDirections)
    {
        for (int i = 0; i < 4; i++)
        {
            inputs[8 + i] = 0f;
        }

        if (targetDirection == Vector2.zero)
        {
            return;
        }

        targetDirection.Normalize();

        for (int i = 0; i < relativeDirections.Length; i++)
        {
            if (Vector2.Dot(targetDirection, relativeDirections[i]) > 0.9f)
            {
                inputs[8 + i] = 1f;
                return;
            }
        }
    }

    private Node FindNearestNode(Vector3 worldPosition, float maxDistance = 1.25f)
    {
        Node nearestNode = null;
        float nearestDistanceSquared = maxDistance * maxDistance;
        Vector2 position = worldPosition;

        for (int i = 0; i < cachedNodes.Count; i++)
        {
            Node node = cachedNodes[i];
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distanceSquared =
                ((Vector2)node.transform.position - position).sqrMagnitude;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestNode = node;
            }
        }

        return nearestNode;
    }

    private void RecordNodeVisit(Node node)
    {
        if (node == null || node == lastRecordedNode)
        {
            return;
        }

        lastRecordedNode = node;
        if (nodeVisitCounts.TryGetValue(node, out int visits))
        {
            nodeVisitCounts[node] = visits + 1;
        }
        else
        {
            nodeVisitCounts[node] = 1;
        }
    }

    private Node FindExplorationTarget(
        Node startNode,
        Dictionary<Node, int> distances,
        Dictionary<Node, Vector2> firstDirections)
    {
        Node bestNode = null;
        float bestScore = float.PositiveInfinity;

        foreach (KeyValuePair<Node, int> pair in distances)
        {
            Node candidate = pair.Key;
            if (candidate == null || candidate == startNode ||
                !firstDirections.TryGetValue(candidate, out Vector2 direction) ||
                direction == Vector2.zero)
            {
                continue;
            }

            int visits = nodeVisitCounts.TryGetValue(candidate, out int count)
                ? count
                : 0;

            // Penaliza volver repetidamente por los mismos nodos cuando no se ha
            // podido asociar ninguna bolita con una ruta válida.
            float score = pair.Value + Mathf.Min(visits, 12) * 2.5f;

            if (nodeGraph.TryGetValue(candidate, out List<NodeEdge> edges))
            {
                int unvisitedNeighbours = 0;
                for (int i = 0; i < edges.Count; i++)
                {
                    if (!nodeVisitCounts.ContainsKey(edges[i].node))
                    {
                        unvisitedNeighbours++;
                    }
                }

                score -= unvisitedNeighbours * 0.75f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestNode = candidate;
            }
        }

        return bestNode;
    }

    private int CountActivePellets()
    {
        if (cachedPellets == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < cachedPellets.Length; i++)
        {
            Pellet pellet = cachedPellets[i];
            if (pellet != null && pellet.gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    private Vector2Int GetGridPosition(Vector3 position)
    {
        return new Vector2Int(
            Mathf.RoundToInt(position.x),
            Mathf.RoundToInt(position.y)
        );
    }

    private bool HasWallBetween(
        Vector2 origin,
        Vector2 direction,
        float distance)
    {
        if (wallLayer.value == 0)
        {
            return false;
        }

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            direction,
            distance,
            wallLayer
        );

        return hit.collider != null && hit.distance + 0.05f < distance;
    }

    private Vector2 GetCurrentDirection()
    {
        Movement movement = GetComponent<Movement>();

        Vector2 currentDirection = movement != null
            ? movement.direction
            : Vector2.zero;

        if (currentDirection == Vector2.zero && movement != null)
        {
            currentDirection = movement.initialDirection;
        }

        if (currentDirection == Vector2.zero)
        {
            currentDirection = Vector2.right;
        }

        return currentDirection.normalized;
    }

    private float CheckWallDistance(
        Vector2 direction,
        float radarDistance)
    {
        if (wallLayer.value == 0)
        {
            return 1f;
        }

        RaycastHit2D hit = Physics2D.Raycast(
            transform.position,
            direction,
            radarDistance,
            wallLayer
        );

        if (hit.collider != null)
        {
            return Mathf.Clamp01(hit.distance / radarDistance);
        }

        return 1f;
    }

    private void AutoConfigureLayers()
    {
        if (gameManager == null)
        {
            gameManager = GetComponentInParent<GameManager>();
        }

        if (wallLayer.value == 0)
        {
            wallLayer = LayerMask.GetMask("Obstacle");
        }
    }
}
