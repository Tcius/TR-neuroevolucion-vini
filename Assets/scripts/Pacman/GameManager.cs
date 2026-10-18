using UnityEngine;

public class GameManager : MonoBehaviour
{
    public Ghost[] ghosts;
    public Pacman pacman;
    public Transform pellets;
    public int score { get; private set; }
    public int ghostMultiplier { get; private set; }

    private void Start()
    {
        NewGame();
    }

    public void NewGame()
    {
        SetScore(0);
        NewRound();
    }

    private void NewRound()
    {
        if (pellets != null)
        {
            foreach (Transform pellet in pellets)
            {
                if (pellet != null)
                {
                    pellet.gameObject.SetActive(true);
                }
            }
        }

        ResetState();
    }

    private void ResetState()
    {
        resetghostmultiplier();

        if (ghosts != null)
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                Ghost ghost = ghosts[i];

                if (ghost == null)
                {
                    continue;
                }

                if (pacman != null)
                {
                    ghost.target = pacman.transform;
                }

                ghost.ResetState();
            }
        }

        if (pacman != null)
        {
            pacman.gameObject.SetActive(true);
            pacman.ResetState();
        }
    }

    private void SetScore(int score)
    {
        this.score = score;
    }

    public void GhostEaten(Ghost ghost)
    {
        if (ghost == null)
        {
            return;
        }

        int pointsAwarded = ghost.points * ghostMultiplier;

        SetScore(this.score + pointsAwarded);

        if (pacman != null)
        {
            AgentController agent =
                pacman.GetComponent<AgentController>();

            if (agent != null)
            {
                agent.RegisterGhostEaten(pointsAwarded);
            }
        }

        this.ghostMultiplier++;
    }

    public void PacmanEaten()
    {
        if (pacman == null)
        {
            return;
        }

        AgentController agent =
            pacman.GetComponent<AgentController>();

        if (agent != null)
        {
            agent.RegisterKilledByGhost();
        }
        else
        {
            pacman.gameObject.SetActive(false);
        }
    }

    public void pelletEaten(Pellet pellet)
    {
        if (pellet == null)
        {
            return;
        }

        pellet.gameObject.SetActive(false);

        SetScore(
            this.score +
            pellet.points
        );

        AgentController agent =
            pacman != null
                ? pacman.GetComponent<AgentController>()
                : null;

        if (agent != null)
        {
            agent.RegisterPelletEaten(
                pellet.points
            );
        }

        if (!HasremainingPellets())
        {
            if (agent != null)
            {
                agent.CompleteMaze();
            }
            else if (pacman != null)
            {
                pacman.gameObject.SetActive(false);
            }
        }
    }

    public void powerPelletEaten(
        PowerPellet pellet)
    {
        if (ghosts != null)
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                Ghost ghost = ghosts[i];

                if (ghost != null &&
                    ghost.frightened != null &&
                    pellet != null)
                {
                    ghost.frightened.Enable(pellet.duration);
                }
            }
        }

        pelletEaten(pellet);

        CancelInvoke();

        if (pellet != null)
        {
            Invoke(
                nameof(resetghostmultiplier),
                pellet.duration
            );
        }
    }

    private bool HasremainingPellets()
    {
        if (pellets == null)
        {
            return false;
        }

        foreach (Transform pellet in pellets)
        {
            if (pellet != null && pellet.gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private void resetghostmultiplier()
    {
        ghostMultiplier = 1;
    }
}