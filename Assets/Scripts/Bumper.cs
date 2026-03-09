using UnityEngine;

public class Bumper : MonoBehaviour
{
    public float bumperStrength;
    public new Light light;
    private float timeLeftLightShine;
    public int lightIntensity;
    public AudioSource audioSource;
    public AudioClip clip;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timeLeftLightShine -= Time.deltaTime;
        if (timeLeftLightShine < 0f)
        {
            light.intensity = 0.1f;
        }
        
    }
    private void OnCollisionEnter(Collision collision)
    {
        collision.collider.GetComponent<Rigidbody>().AddExplosionForce(bumperStrength, transform.position, 8);
        light.intensity = lightIntensity;
        timeLeftLightShine = 0.1f;
        audioSource.Play();
    }
}
