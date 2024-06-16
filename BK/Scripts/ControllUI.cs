using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class ControllUI : MonoBehaviour
{

    #region private members
    private PoseStationTest m_PoseStationScript = null;

    #endregion

    // Start is called before the first frame update
    void Start()
    {
        GameObject pose_station = GameObject.FindWithTag("Pose_Station");
        if (pose_station != null)
        {
            m_PoseStationScript = pose_station.GetComponent<PoseStationTest>();
        }
        Debug.Log("clicked Stop.....");
        
    }

}
