using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Movement))]
public class Pacman : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D circleCollider;
    private Movement movement;

    private Node currentNode;
    private bool waitingForDecision = false;

    public bool NeedsDecision
    {
        get { return waitingForDecision && currentNode != null; }
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        circleCollider = GetComponent<CircleCollider2D>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        float angle = Mathf.Atan2(movement.direction.y, movement.direction.x);
        transform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
    }

    public void ResetState()
    {
        enabled = true;
        spriteRenderer.enabled = true;
        circleCollider.enabled = true;
        movement.ResetState();

        currentNode = null;
        waitingForDecision = false;

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
        if (!NeedsDecision) return;
        if (outputs == null || outputs.Length < 4) return;
        if (currentNode.availableDirections == null || currentNode.availableDirections.Count == 0) return;

        Vector2 currentDirection = movement.direction;

        if (currentDirection == Vector2.zero)
        {
            currentDirection = movement.initialDirection;
        }

        Vector2[] possibleDirections =
        {
            currentDirection,
            new Vector2(-currentDirection.y, currentDirection.x),
            new Vector2(currentDirection.y, -currentDirection.x),
            -currentDirection
        };

        bool hasNonReverseOption = false;

        for (int i = 0; i < 3; i++)
        {
            if (currentNode.availableDirections.Contains(possibleDirections[i]))
            {
                hasNonReverseOption = true;
                break;
            }
        }

        int bestOutputIndex = -1;
        float bestOutputValue = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            Vector2 targetDirection = possibleDirections[i];

            if (!currentNode.availableDirections.Contains(targetDirection))
            {
                continue;
            }

            if (i == 3 && hasNonReverseOption)
            {
                continue;
            }

            if (outputs[i] > bestOutputValue)
            {
                bestOutputValue = outputs[i];
                bestOutputIndex = i;
            }
        }

        if (bestOutputIndex >= 0)
        {
            movement.SetDirection(possibleDirections[bestOutputIndex]);
        }

        waitingForDecision = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Node node = other.GetComponent<Node>();

        if (node != null)
        {
            currentNode = node;
            waitingForDecision = true;
        }
    }
}