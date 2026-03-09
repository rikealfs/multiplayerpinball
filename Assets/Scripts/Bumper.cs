using UnityEngine;

public class Bumper : MonoBehaviour
{
    //strenght of the bumper
    public float bumperStrength;
    //light effect
    public new Light light;
    private float timeLeftLightShine;
    public int lightIntensity;
    //audio
    public AudioSource audioSource;
    public AudioClip clip;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //light effect
        timeLeftLightShine -= Time.deltaTime;
        if (timeLeftLightShine < 0f)
        {
            light.intensity = 0.1f;
        }
        
    }

    //Bumper adds force to the ball and plays a sound and light effect
    private void OnCollisionEnter(Collision collision)
    {
        collision.collider.GetComponent<Rigidbody>().AddExplosionForce(bumperStrength, transform.position, 8);
        light.intensity = lightIntensity;
        timeLeftLightShine = 0.1f;
        audioSource.Play();
    }
}
