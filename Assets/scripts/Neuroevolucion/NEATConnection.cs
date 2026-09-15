[System.Serializable]
public class NEATConnection
{
    public NEATNode fromNode;
    public NEATNode toNode;
    public float weight;
    public bool enabled;

    public NEATConnection(NEATNode fromNode, NEATNode toNode, float weight, bool enabled = true)
    {
        this.fromNode = fromNode;
        this.toNode = toNode;
        this.weight = weight;
        this.enabled = enabled;
    }
}