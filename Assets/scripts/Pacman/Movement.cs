using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Movement : MonoBehaviour
{
    public float speed = 8f;
    public float speedMultiplier = 1f;
    public Vector2 initialDirection;
    public LayerMask obstacleLayer;

    public Rigidbody2D rb { get; private set; }
    public Vector2 direction { get; private set; }
    public Vector2 nextDirection { get; private set; }
    public Vector3 startingPosition { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        startingPosition = transform.position;
    }

    private void Start()
    {
        ResetState();
    }

    public void ResetState()
    {
        speedMultiplier = 1f;
        direction = initialDirection;
        nextDirection = Vector2.zero;
        transform.position = startingPosition;
        rb.bodyType = RigidbodyType2D.Dynamic;
        enabled = true;
    }

    private void Update()
    {
        // Si hay una dirección pendiente y la casilla ya está libre, la aplicamos
        if (nextDirection != Vector2.zero) {
            SetDirection(nextDirection);
        }
    }

    private void FixedUpdate()
    {
        Vector2 position = rb.position;
        Vector2 translation = speed * speedMultiplier * Time.fixedDeltaTime * direction;

        rb.MovePosition(position + translation);
    }

    public void SetDirection(Vector2 newDirection, bool forced = false)
    {
        if (newDirection == Vector2.zero) return;

        if (forced || !Occupied(newDirection))
        {
            this.direction = newDirection;
            nextDirection = Vector2.zero;
        }
        else
        {
            // Si la dirección actual choca contra un muro, no guardamos nextDirection: 
            // cambiamos inmediatamente si la nueva dirección introducida está libre.
            if (Occupied(this.direction))
            {
                if (!Occupied(newDirection))
                {
                    this.direction = newDirection;
                    nextDirection = Vector2.zero;
                }
            }
            else
            {
                nextDirection = newDirection;
            }
        }
    }

    public bool Occupied(Vector2 dir)
    {
        if (dir == Vector2.zero) return false;

        // Reducimos ligeramente el tamaño de la caja (0.5f) y la distancia del rayo (0.6f) 
        // para evitar falsos positivos con las esquinas del mapa
        RaycastHit2D hit = Physics2D.BoxCast(transform.position, Vector2.one * 0.5f, 0f, dir, 0.6f, obstacleLayer);
        return hit.collider != null;
    }
}