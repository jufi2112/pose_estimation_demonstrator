using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TcpControlledBody : MonoBehaviour
{
    #region private members
    private Vector3 m_initialPosition;
    private bool m_setupComplete = false;
    private SMPLX m_smplxScript = null;
    private TcpPuppeteer m_tcpPuppeteerScript = null;
    #endregion

    #region public members
    public int m_interestedBodyID = 1;
    public bool m_connectOnStart = true;
    [Tooltip("Rotation that should be applied to the body immediately before the first pose is applied.")]
    public Vector3 m_initialRotationEulerAngles = new Vector3(-90, 0, 0);
    [Tooltip("Translation that should be applied to the body immediately before the first pose is applied.")]
    public Vector3 m_initialTranslation = new Vector3(0, -0.42f, 0.3f);
    #endregion
    // Start is called before the first frame update
    void Start()
    {
        m_smplxScript = gameObject.GetComponent<SMPLX>();
        // register at "puppeteer" to receive translation and pose updates for the specific body ID
        m_tcpPuppeteerScript = GameObject.FindGameObjectWithTag("TCP_Puppeteer").GetComponent<TcpPuppeteer>();
        if (m_connectOnStart)
            RegisterAtPuppeteer();
    }

    // Update is called once per frame
    void Update()
    {
        
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
            gameObject.transform.Rotate(m_initialRotationEulerAngles);
            gameObject.transform.position += m_initialTranslation;
            m_initialPosition = gameObject.transform.position;
            m_setupComplete = true;
        }
        gameObject.transform.position = m_initialPosition + positionDifferenceData;
        m_smplxScript.SetBodyPose(bodyPose);
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
}
