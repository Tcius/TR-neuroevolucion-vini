[System.Serializable]
public class NEATNode
{
    public int id;
    public float value;
    public float bias;
    public NodeType type;

    public enum NodeType { Input, Hidden, Output }

    public NEATNode(int id, NodeType type, float bias = 0f)
    {
        this.id = id;
        this.type = type;
        this.bias = bias;
        this.value = 0f;
    }
}