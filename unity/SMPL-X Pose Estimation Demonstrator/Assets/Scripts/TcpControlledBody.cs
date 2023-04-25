using System.Collections;
using System.Collections.Generic;
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
    #endregion

    #region public members
    public int m_interestedBodyID = 1;
    public bool m_connectOnStart = true;
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
        // Angles are measured counter-clockwise
        m_bodyRootAngularDifferenceY = DetermineAngularDifferenceY();

        m_smplxScript = gameObject.GetComponent<SMPLX>();
        // register at "puppeteer" to receive translation and pose updates for the specific body ID
        m_tcpPuppeteerScript = GameObject.FindGameObjectWithTag("TCP_Puppeteer").GetComponent<TcpPuppeteer>();
        if (m_connectOnStart)
            RegisterAtPuppeteer();
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
    }

    void OnDestroy()
    {
        bool unregisterSuccess = m_tcpPuppeteerScript.UnregisterBody(gameObject, m_interestedBodyID);
        if (!unregisterSuccess)
            Debug.Log("Could not unregister from the puppeteer");
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
                Debug.Log("Could not register at the puppeteer, puppeteer returned an error");
            return registerSuccess;
        }
        else
        {
            Debug.Log("Could not register at the Puppeteer as no matching script could be found.");
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
            Debug.Log("Could not set body pose: The given array does not have 165 elements!");
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
}
