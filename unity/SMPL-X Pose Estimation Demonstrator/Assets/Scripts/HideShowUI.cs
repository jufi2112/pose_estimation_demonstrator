using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HideShowUI : MonoBehaviour
{
    public InputActionReference pressA_action;
    private Canvas PlayerTable;
    private GameObject PlayerBoard;
    private Canvas EidtorTable_Info;
    private Canvas EidtorTable_Control;
    private ControllUI control_UI_script;

    private void Start()
    {
        if(pressA_action != null)
        {
            pressA_action.action.performed += ToggleMenu;
        }
        PlayerBoard = GameObject.Find("PlayerBoard");
        if (PlayerBoard == null)
        {
            Debug.LogError("PlayerBoard object not found or does not have a Canvas component.");
        }

        PlayerTable = GameObject.Find("PlayerTable").GetComponent<Canvas>();
        // print name of Canvas
        if (PlayerTable == null)
        {
            Debug.LogError("PlayerTable object not found or does not have a Canvas component.");
        }
        EidtorTable_Info = GameObject.Find("EditorTable_Info").GetComponent<Canvas>();
        // print name of Canvas
        if (EidtorTable_Info == null)
        {
            Debug.LogError("EidtorTable_Info object not found or does not have a Canvas component.");
        }
        EidtorTable_Control = GameObject.Find("EditorTable_Control").GetComponent<Canvas>();
        // print name of Canvas
        if (EidtorTable_Control == null)
        {
            Debug.LogError("EidtorTable_Control object not found or does not have a Canvas component.");
        }
        control_UI_script = GameObject.Find("PlayerControl").GetComponent<ControllUI>();
        if (control_UI_script == null)
        {
            Debug.Log("Cannot find PlayerControl.");
        }

    }

    private void ToggleMenu(InputAction.CallbackContext context)
    {
        Debug.Log($"Menu button pressed!");

        PlayerBoard.SetActive(!PlayerBoard.activeSelf);

        /*if (control_UI_script.get_editor_table_state())
        {
            if (PlayerTable != null)
            {
                PlayerTable.enabled = !PlayerTable.enabled;
            }
            if (EidtorTable_Info != null)
            {
                Debug.Log("hide EidtorTable_Info");
                EidtorTable_Info.enabled = !EidtorTable_Info.enabled;
            }
            if (EidtorTable_Control != null)
            {
                Debug.Log("hide EidtorTable_Control");
                EidtorTable_Control.enabled = !EidtorTable_Control.enabled;
            }
        }
        else
        {
            if (PlayerTable != null)
            {
                PlayerTable.enabled = !PlayerTable.enabled;
            }
        }*/
    }
}
