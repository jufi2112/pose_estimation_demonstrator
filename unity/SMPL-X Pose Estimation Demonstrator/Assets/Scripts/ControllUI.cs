using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;


public class ControllUI : MonoBehaviour
{

    #region private members
    private PoseStation m_PoseStationScript = null;
    private Slider progress_slider;
    private bool on_drag = false;
    private bool last_play_state = false;
    #endregion

    // Start is called before the first frame update
    void Start()
    {
        GameObject pose_station = GameObject.FindWithTag("Pose_Station");
        if (pose_station != null)
        {
            m_PoseStationScript = pose_station.GetComponent<PoseStation>();
        }

        /// @ Slider init
        progress_slider = GameObject.Find("MainProgressSlider").GetComponent<Slider>();
        if (progress_slider != null)
        {
            Debug.Log("MainProgressSlider Found: " + progress_slider.name);
            progress_slider.minValue = 0;
            if (m_PoseStationScript != null)
            {
                progress_slider.maxValue = m_PoseStationScript.get_num_frames() / m_PoseStationScript.get_fps();
            }
        }
        else
        {
            Debug.Log("MainProgressSlider not Found!");
        }
    }
    void Update()
    {
        if(!on_drag)
        {
            update_slider();
        }
    }

    ///  @ buttons
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
    public void update_play_frame()
    {
        int new_frame_index = (int)(progress_slider.value * m_PoseStationScript.get_fps());
        if (new_frame_index != m_PoseStationScript.get_playing_frame_index())
        {
            m_PoseStationScript.update_play_frame(new_frame_index);
        }
    }

    /// @ Slider
    public void OnDrag()
    {
        on_drag = true;
        m_PoseStationScript.pause();
        int index = (int)(m_PoseStationScript.get_fps() * progress_slider.value);
        m_PoseStationScript.set_playing_frame_index(index);
    }
    public void OnUp()
    {
        on_drag = false;
        m_PoseStationScript.resume(last_play_state);
    }
    public void OnDown()
    {
        last_play_state = m_PoseStationScript.get_continue_stop();
    }
    private void update_slider()
    {
        float value = m_PoseStationScript.get_playing_frame_index()/m_PoseStationScript.get_fps();
        progress_slider.value = value;
    }
}
