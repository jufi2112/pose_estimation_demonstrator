using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;

public class TcpControlledBody : MonoBehaviour
{
    #region private members
    // The position of the body determined by its placement in the scene
    private Vector3 m_initialPosition;
    // Tells us whether we have extracted all variables related to the initial transformation of the body
    private bool m_setupComplete = false;
    private SMPLX m_smplxScript = null;
    private TcpPuppeteer m_tcpPuppeteerScript = null;
    // Angle between SMPL-X default rotation (defaults to 180 degrees) and the direction the body should face (determined by its y rotation when placed into the scene)
    private float m_bodyRootAngularDifferenceY = 0.0f;
    // Matrix that holds the rotation of the body from default SMPL-X facing direction to the direction the body faces at the start of the scene
    private Matrix4x4 m_homRotMat = Matrix4x4.identity;
    // Keeps track of the game object's rotation so that we can react to it being changed
    private Vector3 m_rotationLastFrame = new Vector3();
    // see https://github.com/vchoutas/smplx/blob/566532a4636d9336403073884dbdd9722833d425/smplx/joint_names.py#L19
    string[] smplxJointNames = new string[] { "pelvis","left_hip","right_hip","spine1","left_knee","right_knee","spine2","left_ankle","right_ankle","spine3", "left_foot","right_foot","neck","left_collar","right_collar","head","left_shoulder","right_shoulder","left_elbow", "right_elbow","left_wrist","right_wrist","jaw","left_eye_smplhf","right_eye_smplhf","left_index1","left_index2","left_index3","left_middle1","left_middle2","left_middle3","left_pinky1","left_pinky2","left_pinky3","left_ring1","left_ring2","left_ring3","left_thumb1","left_thumb2","left_thumb3","right_index1","right_index2","right_index3","right_middle1","right_middle2","right_middle3","right_pinky1","right_pinky2","right_pinky3","right_ring1","right_ring2","right_ring3","right_thumb1","right_thumb2","right_thumb3" };
    private Camera m_povCam = null;
    private Vector3 m_freeCamMoveDirection = new Vector3(0, 0, 0);
    private Vector2 m_freeCamRotateDirection = new Vector2(0, 0);
    private bool b_mouseControlled = true;
    private PlayerInput m_playerInput = null;
    #endregion

    #region public members
    public int m_interestedBodyID = 1;
    public bool m_connectOnStart = true;
    [Tooltip("Whether this body instance should be controlled by the local player")]
    public bool m_belongsToLocalPlayer = false;
    [Tooltip("Camera that should be used for free view")]
    public Camera m_freeCamera = null;
    [Tooltip("Free view camera movement speed")]
    public float m_freeCamMoveSpeed = 10.0f;
    [Tooltip("Free view camera rotation speed")]
    public float m_freeCamRotationSpeed = 1.0f;
    [Tooltip("Rotation that should be applied to the body immediately before the first pose is applied. Order in which the rotations are applied is specified by order in the list.")]
    public List<Vector3> m_initialRotationEulerAngles = new List<Vector3>{new Vector3(-90, 0, 0)};
    [Tooltip("Translation that should be applied to the body immediately before the first pose is applied. This is to align the body with the ground")]
    public Vector3 m_initialTranslation = new Vector3(0, -0.42f, 0.3f);
    [Tooltip("Default Y rotation assumed by the SMPL-X addon. This should most likely be left at 180 degrees")]
    public float m_smplxDefaultRotationY = 180.0f;
    #endregion
    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Transform parent = transform;
        FindAndAssignPOVCameraByTag(parent, "PoV_Camera");
        if (m_belongsToLocalPlayer)
        {
            m_playerInput = gameObject.GetComponent<PlayerInput>();
            if (m_freeCamera == null)
            {
                GameObject[] freeViewCams = GameObject.FindGameObjectsWithTag("Free_Camera");
                if (freeViewCams.Length > 0)
                {
                    m_freeCamera = freeViewCams[0].gameObject.GetComponent<Camera>();
                }
                else
                {
                    Debug.Log("No free view camera reference specified for " + gameObject.name + " and could not find a free view camera in the scene.");
                }
            }
            if (m_povCam == null)
            {
                Debug.Log("Could not find a valid PoV Camera for " + gameObject.name);
                if (m_freeCamera != null)
                {
                    m_freeCamera.gameObject.SetActive(true);
                }
                else
                {
                    Debug.Log("Found no suitable camera");
                }
            }
            else
            {
                m_povCam.gameObject.SetActive(true);
                if (m_freeCamera != null)
                {
                    m_freeCamera.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            if (m_povCam != null)
            {
                m_povCam.gameObject.SetActive(false);
            }
        }
        // Angles are measured counter-clockwise
        m_bodyRootAngularDifferenceY = DetermineAngularDifferenceY();

        m_smplxScript = gameObject.GetComponent<SMPLX>();
        // register at "puppeteer" to receive translation and pose updates for the specific body ID
        GameObject obj = GameObject.FindWithTag("TCP_Puppeteer");
        if (obj != null)
        {
            m_tcpPuppeteerScript = obj.GetComponent<TcpPuppeteer>();
        }
        if (m_tcpPuppeteerScript is null)
        {
            Debug.Log(gameObject.name + " could not find a valid TCPPuppeteer instance.");
        }
        else
        {
            if (m_connectOnStart)
                RegisterAtPuppeteer();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (m_setupComplete)
        {
            if (gameObject.transform.localRotation.eulerAngles != m_rotationLastFrame)
            {
                Debug.Log("Local rotation of " + gameObject.name + " has changed, rebuilding rotation matrix");
                m_bodyRootAngularDifferenceY = DetermineAngularDifferenceY();
                m_homRotMat = BuildHomogeneousRotationMatrixY(m_bodyRootAngularDifferenceY);
                m_rotationLastFrame = gameObject.transform.localRotation.eulerAngles;
            }
        }
        if (m_freeCamera != null && m_freeCamera.gameObject.activeSelf)
        {
            m_freeCamera.gameObject.transform.Translate(m_freeCamMoveDirection * m_freeCamMoveSpeed * Time.deltaTime);
            float dt = b_mouseControlled ? 1f : Time.deltaTime;
            m_freeCamera.gameObject.transform.Rotate(new Vector3(m_freeCamRotateDirection.y, m_freeCamRotateDirection.x, 0.0f) * m_freeCamRotationSpeed * dt);
            Transform freeCamTransform = m_freeCamera.gameObject.transform;
            freeCamTransform.rotation = Quaternion.Euler(ClampCamXRotation(freeCamTransform.rotation.eulerAngles.x, 85f, -85f), freeCamTransform.rotation.eulerAngles.y, 0.0f);
        }
    }

    void OnDestroy()
    {
        if (m_tcpPuppeteerScript != null)
        {
            bool unregisterSuccess = m_tcpPuppeteerScript.UnregisterBody(gameObject, m_interestedBodyID);
            if (!unregisterSuccess)
                Debug.Log("Could not unregister " + gameObject.name + " from the puppeteer!");
        }
    }

    public void SetParameters(Vector3 positionDifferenceData, float[] bodyPose)
    {
        if (!m_setupComplete)
        {
            // Build 3x3 rotation matrix to transform the position vector into the direction the body faced at start up
            // Unity only supports 4x4 matrices by default, so build homogeneous matrix
            m_homRotMat = BuildHomogeneousRotationMatrixY(m_bodyRootAngularDifferenceY);
            // Apply rotation necessary for the received rotations to work in Unity, may be deleted once all producers send data in Unity's coordinate system
            foreach (Vector3 rotation in m_initialRotationEulerAngles)
                gameObject.transform.Rotate(rotation);
            gameObject.transform.position += m_initialTranslation;
            m_initialPosition = gameObject.transform.position;
            m_rotationLastFrame = gameObject.transform.localRotation.eulerAngles;
            m_setupComplete = true;
        }
        // position if the body would have a rotation of y=180 degrees
        Vector4 homPositionDiff = new Vector4(positionDifferenceData.x, positionDifferenceData.y, positionDifferenceData.z, 1.0f);
        Vector3 rotatedPosDiff = m_homRotMat * homPositionDiff;
        gameObject.transform.position = m_initialPosition + rotatedPosDiff;
        bool status = SetBodyPose(bodyPose);
    }

    public bool RegisterAtPuppeteer()
    {
        if (m_tcpPuppeteerScript != null)
        {
            bool registerSuccess = m_tcpPuppeteerScript.RegisterBody(gameObject, m_interestedBodyID);
            if (!registerSuccess)
                Debug.Log("Could not register " + gameObject.name + " at the puppeteer because it returned an error");
            return registerSuccess;
        }
        else
        {
            Debug.Log("Could not register " + gameObject.name + " at the Puppeteer, as no matching script could be found.");
            return false;
        }
    }

    private Matrix4x4 BuildHomogeneousRotationMatrixY(float rotationAngleDeg)
    {
        Matrix4x4 mat = Matrix4x4.identity;
        float rotationRad = rotationAngleDeg * Mathf.Deg2Rad;
        mat[0, 0] = Mathf.Cos(rotationRad);
        mat[0, 2] = - Mathf.Sin(rotationRad);
        mat[2, 0] = Mathf.Sin(rotationRad);
        mat[2, 2] = Mathf.Cos(rotationRad);
        return mat;
    }

    private float DetermineAngularDifferenceY()
    {
        return m_smplxDefaultRotationY - gameObject.transform.localRotation.eulerAngles.y;
    }

    private bool SetBodyPose(float[] pose)
    {
        if (m_smplxScript == null)
        {
            return false;
        }
        if (pose.Length != 165)
        {
            Debug.Log(gameObject.name + ": Could not set body pose: The given array does not have 165 elements!");
            return false;
        }
        for (int i = 0; i < 55; ++i)
        {
            string jointName = smplxJointNames[i];
            float rodX = pose[i*3 + 0];
            float rodY = pose[i*3 + 1];
            float rodZ = pose[i*3 + 2];
            Quaternion quat = SMPLX.QuatFromRodrigues(rodX, rodY, rodZ);
            m_smplxScript.SetLocalJointRotation(jointName, quat);
        }
        m_smplxScript.UpdatePoseCorrectives();
        m_smplxScript.UpdateJointPositions(true);
        return true;
    }

    private bool FindAndAssignPOVCameraByTag(Transform parent, string tag)
    {
        for (int i = 0; i < parent.childCount; ++i)
        {
            Transform child = parent.GetChild(i);
            if (child.tag == tag)
            {
                m_povCam = child.gameObject.GetComponent<Camera>();
                return true;
            }
            if (child.childCount > 0)
            {
                if (FindAndAssignPOVCameraByTag(child, tag))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private float ClampCamXRotation(float eulerAngle, float maxDownAngle=90f, float maxUpAngle=-90f)
    {
        // X upwards angle is negative -1 -> 359
        maxUpAngle = PositiveMod(maxUpAngle, 360);
        maxDownAngle = PositiveMod(maxDownAngle, 360);
        eulerAngle = PositiveMod(eulerAngle, 360);
        if (eulerAngle > maxDownAngle && eulerAngle < maxUpAngle)
        {
            if ((eulerAngle - maxDownAngle) <= (maxUpAngle - eulerAngle))
                // closer to max down angle
                eulerAngle = maxDownAngle;
            else
                eulerAngle = maxUpAngle;
        }
        return eulerAngle;
    }

    private float PositiveMod(float x, float m)
    {
        return (x%m + m) % m;
    }

    public void OnSwitch_Camera()
    {
        if (m_povCam != null && m_freeCamera != null)
        {
            m_povCam.gameObject.SetActive(!m_povCam.gameObject.activeSelf);
            m_freeCamera.gameObject.SetActive(!m_freeCamera.gameObject.activeSelf);
        }
    }

    public void OnMove_Camera(InputValue value)
    {
        m_freeCamMoveDirection = value.Get<Vector3>();
    }

    public void OnRotate_Camera(InputValue value)
    {
        m_freeCamRotateDirection = value.Get<Vector2>();
    }

    public void OnControlsChanged(PlayerInput input)
    {
        if (m_belongsToLocalPlayer)
        {
            b_mouseControlled = input.currentControlScheme.Equals("Keyboard & Mouse");
        }
    }
}
