using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class UIController : MonoBehaviour
{
    public InputActionReference menu;
    public Canvas controlUICanvas;

    private void Start()
    {
        GameObject controlUIObject = GameObject.Find("Complete XR Origin Set Up/XR Origin/LeftHand (Smooth locomotion)/Control_UI");
        if(controlUIObject != null)
        {
            controlUICanvas = controlUIObject.GetComponent<Canvas>();
        }
        if(menu != null)
        {
            menu.action.Enable();
            menu.action.performed += ToggleMenu;
        }
        
        // print name of Canvas
        if (controlUICanvas != null)
        {
            Debug.Log("Canvas Name: " + controlUICanvas.name);
        }
        else
        {
            Debug.LogError("Control_UI object not found or does not have a Canvas component.");
        }

    }

    private void ToggleMenu(InputAction.CallbackContext context)
    {
        controlUICanvas.enabled = !controlUICanvas.enabled;
    }
}
