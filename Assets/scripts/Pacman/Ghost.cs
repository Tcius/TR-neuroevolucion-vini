using UnityEngine;

[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(Movement))]
public class Ghost : MonoBehaviour
{
    public Movement movement { get; private set; }
    public GhostHome home { get; private set; }
    public GhostScatter scatter { get; private set; }
    public GhostChase chase { get; private set; }
    public GhostFrightened frightened { get; private set; }
    public GhostBehavior initialBehavior;
    public Transform target;
    public int points = 200;

    private void Awake()
    {
        movement = GetComponent<Movement>();
        home = GetComponent<GhostHome>();
        scatter = GetComponent<GhostScatter>();
        chase = GetComponent<GhostChase>();
        frightened = GetComponent<GhostFrightened>();
    }

    private void Start()
    {
        ResetState();
    }

    public void ResetState()
    {
        gameObject.SetActive(true);

        GameManager gameManager =
            GetComponentInParent<GameManager>();

        if (gameManager != null &&
            gameManager.pacman != null)
        {
            target = gameManager.pacman.transform;
        }

        if (movement != null)
        {
            movement.ResetState();
        }

        if (frightened != null)
        {
            frightened.Disable();
        }

        if (chase != null)
        {
            chase.Disable();
        }

        if (scatter != null)
        {
            scatter.Enable();
        }

        if (home != null && home != initialBehavior)
        {
            home.Disable();
        }

        if (initialBehavior != null)
        {
            initialBehavior.Enable();
        }
    }

    public void SetPosition(Vector3 position)
    {
        position.z = transform.position.z;
        transform.position = position;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer != LayerMask.NameToLayer("Pacman"))
        {
            return;
        }

        GameManager gameManager =
            GetComponentInParent<GameManager>();

        if (gameManager == null)
        {
            return;
        }

        if (frightened != null && frightened.enabled)
        {
            gameManager.GhostEaten(this);
        }
        else
        {
            gameManager.PacmanEaten();
        }
    }
}