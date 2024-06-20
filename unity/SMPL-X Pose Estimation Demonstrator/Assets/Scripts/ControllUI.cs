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
    private Text total_time;
    private Text now_time;
    private Dropdown boost_rate_dropdown;
    private Dropdown file_name_dropdown;

    private bool on_drag = false;
    private bool last_play_state = false;
    #endregion

    // Start is called before the first frame update
    void Start()
    {
        // Get Pose Station
        GameObject pose_station = GameObject.FindWithTag("Pose_Station");
        if (pose_station != null)
        {
            m_PoseStationScript = pose_station.GetComponent<PoseStation>();
        }

        // Get Main Progress Slider
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

        // Get Total Time Text
        total_time = GameObject.Find("TotalTime").GetComponent<Text>();
        if (total_time != null && m_PoseStationScript != null)
        {
            float total_time_sec = m_PoseStationScript.get_num_frames() / m_PoseStationScript.get_fps();

            total_time.text = FormatTime(total_time_sec);
        }
        else
        {
            Debug.Log("TotalTime Text not Found!");
        }

        // Get Now Time Text
        now_time = GameObject.Find("NowTIme").GetComponent<Text>();
        if(now_time != null && m_PoseStationScript != null)
        {
            now_time.text = "00:00";
        }
        else
        {
            Debug.Log("NowTime Text not Found!");
        }

        // Get Boost Rate Dropdown
        boost_rate_dropdown = GameObject.Find("BoostRateDropdown").GetComponent<Dropdown>();
        if(boost_rate_dropdown != null) 
        {
            boost_rate_dropdown.value = 2;
        }
        else
        {
            Debug.Log("BoostRateDropdown not Found!");
        }

        // Get File Name Dropdown
        file_name_dropdown = GameObject.Find("FileNameDropdown").GetComponent<Dropdown>();
        if (file_name_dropdown != null)
        {
            file_name_dropdown.AddOptions(m_PoseStationScript.get_npz_files());
            file_name_dropdown.value = 0;
        }
        else
        {
            Debug.Log($"in {this.name} FileNameDropdown not Found!");
        }

    }
    void Update()
    {
        if(!on_drag)
        {
            update_slider();
        }
        update_play_time_text();
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

    /// @ Slider
    public void OnDrag()
    {
        on_drag = true;
        m_PoseStationScript.pause();
        m_PoseStationScript.set_playing_timer(progress_slider.value);
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
    private void update_play_time_text()
    {
        float playing_time = m_PoseStationScript.get_playing_frame_index() / m_PoseStationScript.get_fps();
        now_time.text = FormatTime(playing_time);
    }

    /// @ Boost Rate Dropdown
    public void update_boost_rate()
    {
        float boost = m_PoseStationScript.get_booste_rate();
        if (boost_rate_dropdown.value == 0)
        {
            boost = 0.25f;
        }
        else if (boost_rate_dropdown.value == 1)
        {
            boost = 0.5f;
        }
        else if (boost_rate_dropdown.value == 2)
        {
            boost = 1f;
        }
        else if (boost_rate_dropdown.value == 3)
        {
            boost = 1.5f;
        }
        else if (boost_rate_dropdown.value > 3 || boost_rate_dropdown.value < 11)
        {
            boost = boost_rate_dropdown.value - 2;
        }
        else
        {
            Debug.Log("BoostRateDropdown Value Not Valide");
        }
        if(boost != m_PoseStationScript.get_booste_rate())
        {
            m_PoseStationScript.set_boost_rate(boost);
        }
    }
    private string FormatTime(float totalSeconds)
    {
        int hours = (int)totalSeconds / 3600;
        int minutes = ((int)totalSeconds % 3600) / 60;
        float remainingSeconds = totalSeconds % 60;

        string formattedTime = string.Empty;

        if (hours > 0)
        {
            formattedTime += $"{hours:D2}:";
        }

        formattedTime += $"{minutes:D2}:{remainingSeconds:00.0}";

        return formattedTime;
    }

    /// @ File Name Dropdown
    public void change_file()
    {
        m_PoseStationScript.change_file(file_name_dropdown.value);
        float total_record_time = m_PoseStationScript.get_num_frames() / m_PoseStationScript.get_fps();
        total_time.text = FormatTime(total_record_time);
        progress_slider.maxValue = total_record_time;
    }
}
