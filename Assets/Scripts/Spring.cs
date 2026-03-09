using UnityEngine;

public class Spring : MonoBehaviour
{
 
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Called when shoot input is received, adds force to the ball if it is on the spring
    void OnShoot()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, 0.5f);
        foreach (Collider collider in colliders) 
        { 
            Ball ball = collider.GetComponent<Ball>();
            if (ball != null)
            {
                collider.GetComponent<Ball>().Shoot();
            }
        }
    }
}
