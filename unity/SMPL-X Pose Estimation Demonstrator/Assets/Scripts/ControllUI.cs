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
    private Canvas editor_control_canvas;
    private Canvas editor_info_canvas;
    private InputField start_point_input;
    private InputField end_point_input;
    private InputField copied_file_input;
    private Toggle copy_shape_toggle;
    private ToggleGroup body_toggle_group;
    private Toggle body1, body2, body3, body4;
    private float slice_start_time = 0.0f;
    private float slice_end_time = 0.0f;
    private bool editor_table_state = false;

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
                progress_slider.maxValue = m_PoseStationScript.get_num_frames(0) / m_PoseStationScript.get_fps();
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
            float total_time_sec = m_PoseStationScript.get_num_frames(0) / m_PoseStationScript.get_fps();

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

        // Get Editor Control Canvas
        editor_control_canvas = GameObject.Find("EditorTable_Control").GetComponent<Canvas>();
        if (editor_control_canvas != null)
        {
            editor_control_canvas.enabled = false;
        }
        else
        {
            Debug.Log($"in {this.name} EditorTable_Control not Found!");
        }
        
        // Get Start and End Point input.
        start_point_input = GameObject.Find("StartPoint").GetComponent<InputField>();
        if (start_point_input != null)
        {
            start_point_input.text = "00:00:00";
        }
        else
        {
            Debug.Log($"in {this.name} StartPoint not Found!");
        }
        end_point_input = GameObject.Find("EndPoint").GetComponent<InputField>();
        if (end_point_input != null)
        {
            end_point_input.text = "00:00:00";
        }
        else
        {
            Debug.Log($"in {this.name} EndPoint not Found!");
        }

        // Get Editor Info Canvas
        editor_info_canvas = GameObject.Find("EditorTable_Info").GetComponent<Canvas>();
        if (editor_info_canvas != null)
        {
            editor_info_canvas.enabled = false;
        }
        else
        {
            Debug.Log($"in {this.name} EditorTable_Info not Found!");
        }

        // Get Copied Field input.
        copied_file_input = GameObject.Find("CopiedFile").GetComponent<InputField>();
        if(copied_file_input != null)
        {
            copied_file_input.text = "Copied: ";
        }
        else
        {
            Debug.Log($"in {this.name} CopiedFile not Found!");
        }

        // Get Copy Shape Toggle
        copy_shape_toggle = GameObject.Find("CopyShapeToggle").GetComponent<Toggle>();
        if (copy_shape_toggle != null)
        {
            copy_shape_toggle.isOn = true;
        }
        else
        {
            Debug.Log($"in {this.name} CopyShapeToggle not Found!");
        }

        // Get Body Toggles
        body1 = GameObject.Find("Body1").GetComponent<Toggle>();
        if(body1==null) { Debug.Log($"in {this.name} Body1 Toggle not Found!"); }
        body2 = GameObject.Find("Body2").GetComponent<Toggle>();
        if (body2 == null) { Debug.Log($"in {this.name} Body2 Toggle not Found!"); }
        body3 = GameObject.Find("Body3").GetComponent<Toggle>();
        if (body3 == null) { Debug.Log($"in {this.name} Body3 Toggle not Found!"); }
        body4 = GameObject.Find("Body4").GetComponent<Toggle>();
        if (body4 == null) { Debug.Log($"in {this.name} Body4 Toggle not Found!"); }
        int num_body = m_PoseStationScript.get_num_body();
        if (num_body == 4)
        {
            body1.isOn = true;
        }
        else if (num_body == 3)
        {
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else if (num_body == 2)
        {
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else if (num_body == 1)
        {
            body2.gameObject.SetActive(false);
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else
        {
            body1.gameObject.SetActive(false);
            body2.gameObject.SetActive(false);
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
        }

        // Get Body Toggle Group
        body_toggle_group = GameObject.Find("BodyToggleGroup").GetComponent<ToggleGroup>();
        if (body_toggle_group != null)
        {
            
        }
        else
        {
            Debug.Log($"in {this.name} BodyToggleGroup not Found!");
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
    private string FormatTimeShort(float totalSeconds)
    {
        int hours = (int)totalSeconds / 3600;
        int minutes = ((int)totalSeconds % 3600) / 60;
        float remainingSeconds = totalSeconds % 60;

        string formattedTime = string.Empty;

        if (hours > 0)
        {
            formattedTime += $"{hours:D2}:";
        }
        if(minutes > 0) 
        {
            formattedTime += $"{minutes:D2}";
        }

        formattedTime += $":{remainingSeconds:00.0}";

        return formattedTime;
    }

    /// @ File Name Dropdown
    public void change_file()
    {
        m_PoseStationScript.change_file(file_name_dropdown.value);
        float total_record_time = m_PoseStationScript.get_num_frames(0) / m_PoseStationScript.get_fps(); // --------- PTC
        total_time.text = FormatTime(total_record_time);
        progress_slider.maxValue = total_record_time;
        start_point_input.text = "00:00:00";
        end_point_input.text = "00:00:00";
        slice_start_time = 0.0f;
        slice_end_time = 0.0f;

        int num_body = m_PoseStationScript.get_num_body();
        Debug.Log($"-------------{num_body}");
        if (num_body == 4)
        {
            body1.isOn = true;
        }
        else if (num_body == 3)
        {
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else if (num_body == 2)
        {
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else if (num_body == 1)
        {
            body2.gameObject.SetActive(false);
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
            body1.isOn = true;
        }
        else
        {
            body1.gameObject.SetActive(false);
            body2.gameObject.SetActive(false);
            body3.gameObject.SetActive(false);
            body4.gameObject.SetActive(false);
        }
    }

    /// @ Editor Table Control
    public void show_hide_editor_control()
    {
        if (editor_table_state == true)
        {
            editor_control_canvas.enabled = false;
            editor_info_canvas.enabled = false;
            editor_table_state = false;
        }
        else
        {
            editor_control_canvas.enabled = true;
            editor_info_canvas.enabled = true;
            editor_table_state = true;
        }
    }
    public bool get_editor_table_state() { return editor_table_state; }  
    /// @ Copy and Paste Function
    public void choose_start_point()
    {
        if (progress_slider.value < slice_end_time)
        {
            slice_start_time = slice_end_time;
        }
        else
        {
            slice_start_time = progress_slider.value;
        }
        start_point_input.text = FormatTime(slice_start_time);
    }
    public void cancel_start_choose()
    {
        slice_start_time = 0.0f;
        start_point_input.text = "00:00:00";
    }
    public void choose_end_point()
    {
        if (progress_slider.value < slice_start_time)
        {
            slice_end_time = slice_start_time;
        }
        else
        {
            slice_end_time = progress_slider.value;
        }
        end_point_input.text = FormatTime(slice_end_time);
    }
    public void cancel_end_choose()
    {
        slice_end_time = 0.0f;
        end_point_input.text = "00:00:00";
    }

    public void copy_slice()
    {
        if(slice_end_time < slice_start_time)
        {
            throw new Exception("End Point should not be less than Start Point");
        }
        else
        {
            m_PoseStationScript.copy_slice(slice_start_time, slice_end_time, copy_shape_toggle.isOn);

            string copied_file_text = "Copied: " + m_PoseStationScript.get_playing_filename();

            copied_file_input.text = copied_file_text;
        }
    }
    public void cut_slice()
    {
        if (slice_end_time < slice_start_time)
        {
            throw new Exception("End Point should not be less than Start Point");
        }
        else
        {
            m_PoseStationScript.pause();
            string copied_file_text = "Copied: " + m_PoseStationScript.get_playing_filename();
            m_PoseStationScript.cut_slice(slice_start_time, slice_end_time, copy_shape_toggle.isOn);
            float new_total_time = m_PoseStationScript.get_num_frames(m_PoseStationScript.get_playing_body_id()) / m_PoseStationScript.get_fps(); // --------- PTC
            if (progress_slider.value > new_total_time)
            {
                progress_slider.value = new_total_time;
                m_PoseStationScript.set_playing_timer(new_total_time);
            }
            progress_slider.maxValue = new_total_time;
            total_time.text = FormatTime(new_total_time);
            copied_file_input.text = copied_file_text;
            m_PoseStationScript.resume(last_play_state);
        }
    }
    public void paste_slice()
    {
        int insert_index = (int)(progress_slider.value * m_PoseStationScript.get_fps());

        m_PoseStationScript.paste_slice(insert_index, copy_shape_toggle.isOn);

        float new_total_time = m_PoseStationScript.get_num_frames(m_PoseStationScript.get_playing_body_id()) / m_PoseStationScript.get_fps(); // --------- PTC
        progress_slider.maxValue = new_total_time;
        total_time.text = FormatTime(new_total_time);
    }
    public void replace_slice()
    {
        if (slice_end_time < slice_start_time)
        {
            throw new Exception("End Point should not be less than Start Point");
        }
        else
        {
            int replace_start_index = (int)(slice_start_time * m_PoseStationScript.get_fps());
            int replace_end_index = (int)(slice_end_time * m_PoseStationScript.get_fps());
                        
            m_PoseStationScript.pause();
            progress_slider.value = slice_start_time;
            m_PoseStationScript.replace_slice(replace_start_index, replace_end_index, copy_shape_toggle.isOn); 
            float new_total_time = m_PoseStationScript.get_num_frames(m_PoseStationScript.get_playing_body_id()) / m_PoseStationScript.get_fps(); // --------- PTC
            progress_slider.maxValue = new_total_time;
            m_PoseStationScript.set_playing_timer(progress_slider.value);
            m_PoseStationScript.resume(last_play_state);
            total_time.text = FormatTime(new_total_time);
        }

    }

    public void toggle_body1()
    {
        m_PoseStationScript.set_playing_body_id(0);
    }
    public void toggle_body2()
    {
        m_PoseStationScript.set_playing_body_id(1);
    }
    public void toggle_body3()
    {
        m_PoseStationScript.set_playing_body_id(2);
    }
    public void toggle_body4()
    {
        m_PoseStationScript.set_playing_body_id(3);
    }
}
