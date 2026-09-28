using UnityEngine;
using TMPro;

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
        foreach (Transform pellet in this.pellets)
        {
            pellet.gameObject.SetActive(true);
        }

        ResetState();
    }

    private void ResetState()
    {
        resetghostmultiplier();

        this.pacman.gameObject.SetActive(true);
        pacman.ResetState();
    }

    private void SetScore(int score)
    {
        this.score = score;
    }

    public void GhostEaten(Ghost ghost)
    {
        SetScore(
            this.score +
            ghost.points *
            this.ghostMultiplier
        );

        this.ghostMultiplier++;
    }

    public void PacmanEaten()
    {
        this.pacman.gameObject.SetActive(false);
    }

    public void pelletEaten(Pellet pellet)
    {
        pellet.gameObject.SetActive(false);

        SetScore(
            this.score +
            pellet.points
        );

        if (!HasremainingPellets())
        {
            AgentController agent =
                pacman.GetComponent<AgentController>();

            if (agent != null)
            {
                agent.CompleteMaze();
            }
            else
            {
                this.pacman.gameObject.SetActive(false);
            }
        }
    }

    public void powerPelletEaten(
        PowerPellet pellet)
    {
        pelletEaten(pellet);

        CancelInvoke();

        Invoke(
            nameof(resetghostmultiplier),
            pellet.duration
        );
    }

    private bool HasremainingPellets()
    {
        foreach (Transform pellet in this.pellets)
        {
            if (pellet.gameObject.activeSelf)
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