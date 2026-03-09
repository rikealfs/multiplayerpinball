using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Pinball : NetworkBehaviour
{
    //public static Pinball Instance;
    public float hitStrenght = 8000f;
    public float dampening = 250f;

    public HingeJoint leftHinge;
    public HingeJoint rightHinge;

    private JointSpring jointSpringReleased = new();
    private JointSpring jointSpringPressed = new();

    private NetworkVariable<bool> LeftPressed = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> RightPressed = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );


    public GameObject weakBumper;
    public GameObject mediumBumper;
    public GameObject strongBumper;
    public GameObject peg;

    public GameObject position1;
    public GameObject position2;
    public GameObject position3;
    public GameObject position4;
    public GameObject position5;
    public GameObject position6;
    public GameObject position7;

    public AudioSource audioSource;

    private bool lastLeft;
    private bool lastRight;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        jointSpringPressed.spring = jointSpringReleased.spring = hitStrenght;
        jointSpringPressed.damper = jointSpringReleased.damper = dampening;

        jointSpringPressed.targetPosition = leftHinge.limits.max;
        jointSpringReleased.targetPosition = leftHinge.limits.min;
    }

    private void OnLeftFlipper(InputValue value)
    {
        if (!IsOwner) return;

        bool pressed = value.isPressed;
        // send only on change
        if (pressed == lastLeft) return;  
        lastLeft = pressed;

        SetLeftServerRpc(pressed);

        if (audioSource) audioSource.Play();
    }

    private void OnRightFlipper(InputValue value)
    {
        if (!IsOwner) return;

        bool pressed = value.isPressed;
        // send only on change
        if (pressed == lastRight) return;
        lastRight = pressed;

        SetRightServerRpc(pressed);

        if (audioSource) audioSource.Play();
    }

    //Server RCPs
    [ServerRpc]
    private void SetLeftServerRpc(bool pressed) => LeftPressed.Value = pressed;

    [ServerRpc]
    private void SetRightServerRpc(bool pressed) => RightPressed.Value = pressed;


    // Update is called once per frame
    private void FixedUpdate()
    {
        leftHinge.spring = LeftPressed.Value ? jointSpringPressed : jointSpringReleased;
        rightHinge.spring = RightPressed.Value ? jointSpringPressed : jointSpringReleased;
    }
    
    public void AddElement(int elementID,int positionID)
    {
        return;
        Transform positionTransform = null;
        if (positionID == 1)
        {
            positionTransform = position1.transform;
        }
        if (positionID == 2)
        {
            positionTransform = position2.transform;
        }
        if (positionID == 3)
        {
            positionTransform = position3.transform;
        }
        if (positionID == 4)
        {
            positionTransform = position4.transform;
        }
        if (positionID == 5)
        {
            positionTransform = position5.transform;
        }
        if (positionID == 6)
        {
            positionTransform = position6.transform;
        }
        if (positionID == 7)
        {
            positionTransform = position7.transform;
        }
        if (positionTransform != null)
        {
            if (elementID == 1)
            {
                Instantiate(weakBumper, positionTransform);
            }
            if (elementID == 2)
            {
                Instantiate(mediumBumper, positionTransform);
            }
            if (elementID == 3)
            {
                Instantiate(strongBumper, positionTransform);
            }
            if (elementID == 4)
            {
                Instantiate(peg, positionTransform);
            }
        }
       
    }
}
