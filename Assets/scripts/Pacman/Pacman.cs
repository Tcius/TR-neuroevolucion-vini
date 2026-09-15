using UnityEngine;
[RequireComponent(typeof(Movement))]
public class Pacman : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D circleCollider;
    private Movement movement;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        circleCollider = GetComponent<CircleCollider2D>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        // if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) {
        //     UP();
        // }
        // else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) {
        //     DOWN();
        // }
        // else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) {
        //     LEFT();
        // }
        // else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) {
        //     RIGHT();
        // }

        float angle = Mathf.Atan2(movement.direction.y, movement.direction.x);
        transform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
    }
    public void ResetState()
    {
        enabled = true;
        spriteRenderer.enabled = true;
        circleCollider.enabled = true;
        movement.ResetState();
        gameObject.SetActive(true);
    }

    public void UP()
    {
        movement.SetDirection(Vector2.up);
    }
    public void DOWN()
    {
        movement.SetDirection(Vector2.down);
    }
    public void LEFT()
    {
        movement.SetDirection(Vector2.left);
    }
    public void RIGHT()
    {
        movement.SetDirection(Vector2.right);
    }
    


    public void ProcessOutputs(float[] outputs)
    {
        if (outputs == null || outputs.Length < 4) return;

        int maxIndex = 0;
        float maxValue = outputs[0];

        for (int i = 1; i < outputs.Length; i++)
        {
            if (outputs[i] > maxValue)
            {
                maxValue = outputs[i];
                maxIndex = i;
            }
        }

        Vector2 direction = Vector2.zero;
        switch (maxIndex)
        {
            case 0: direction = Vector2.up; break;
            case 1: direction = Vector2.down; break;
            case 2: direction = Vector2.left; break;
            case 3: direction = Vector2.right; break;
        }

        // Llama al componente Movement asignado en el script
        if (this.movement != null)
        {
            this.movement.SetDirection(direction);
        }
    }
}
