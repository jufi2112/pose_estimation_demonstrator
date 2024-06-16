using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;


public class ControllUI : MonoBehaviour
{

    #region private members
    private PoseStation m_PoseStationScript = null;

    #endregion

    // Start is called before the first frame update
    void Start()
    {
        GameObject pose_station = GameObject.FindWithTag("Pose_Station");
        if (pose_station != null)
        {
            m_PoseStationScript = pose_station.GetComponent<PoseStation>();
        }
       
        
    }
    public void StopContinue()
    {
        m_PoseStationScript.ContinueStop();
    }
    public void Backward()
    {
        m_PoseStationScript.Rewind();
    }
    public void Forward()
    {
        m_PoseStationScript.FastForward();
    }

}
