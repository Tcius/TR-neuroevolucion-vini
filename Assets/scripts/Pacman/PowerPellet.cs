using UnityEngine;

public class PowerPellet : Pellet
{
    public float duration = 8f;

    protected override void Eat()
    {
        transform.parent.transform.parent.transform.parent.GetComponent<GameManager>().powerPelletEaten(this);
    }

}
