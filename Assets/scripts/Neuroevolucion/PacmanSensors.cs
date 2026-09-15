using UnityEngine;

public class PacmanSensors : MonoBehaviour
{
    public LayerMask wallLayer;
    public LayerMask ghostLayer;
    public float maxRadarDistance = 10f;

    public float[] GetInputs(Transform closestPellet, Transform closestBigPellet, bool ghostsAreScared)
    {
        float[] inputs = new float[13];

        inputs[0] = CheckLayerDistance(Vector2.up, wallLayer);
        inputs[1] = CheckLayerDistance(Vector2.down, wallLayer);
        inputs[2] = CheckLayerDistance(Vector2.left, wallLayer);
        inputs[3] = CheckLayerDistance(Vector2.right, wallLayer);

        inputs[4] = CheckLayerDistance(Vector2.up, ghostLayer);
        inputs[5] = CheckLayerDistance(Vector2.down, ghostLayer);
        inputs[6] = CheckLayerDistance(Vector2.left, ghostLayer);
        inputs[7] = CheckLayerDistance(Vector2.right, ghostLayer);

        if (closestPellet != null)
        {
            Vector2 directionToPellet = (closestPellet.position - transform.position).normalized;
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
            Vector2 directionToBigPellet = (closestBigPellet.position - transform.position).normalized;
            inputs[10] = directionToBigPellet.x;
            inputs[11] = directionToBigPellet.y;
        }
        else
        {
            inputs[10] = 0f;
            inputs[11] = 0f;
        }

        inputs[12] = ghostsAreScared ? 1f : 0f;

        return inputs;
    }

    private float CheckLayerDistance(Vector2 direction, LayerMask layer)
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, direction, maxRadarDistance, layer);

        if (hit.collider != null)
        {
            return 1f - (hit.distance / maxRadarDistance);
        }

        return 0f;
    }
}