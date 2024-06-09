using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using System;
//// Support for reading .npz
using Ionic.Zip;
using ns = NumSharp;
using np = Numpy;

public class PoseStationTest : MonoBehaviour
{
    private string relativePath = "../../../dataset/MoSh/50002/jumping_jacks_stageii.npz";
    private bool single_shape_paramenters = true;
    private ns.NDArray poses;
    private ns.NDArray shapes;
    private ns.NDArray transl;
    private ns.NDArray fps = null;
    // Start is called before the first frame update
    void Start()
    {        
        string projectRootPath = Application.dataPath;

        // The whole path
        string npz_file = Path.GetFullPath(Path.Combine(projectRootPath, relativePath));

        if (File.Exists(npz_file))
        {
           (poses,shapes,transl,fps) = _load_npz_attribute(npz_file, "poses", "betas", "trans", "mocap_frame_rate");
            // Debug.Log("poses: " + string.Join(",", poses_array));
            // Debug.Log("shapes: " + string.Join(",", shapes_array));
            // Debug.Log("transl: " + string.Join(",", transl_array));
            // Debug.Log("mocap_fps: " + mocap_fps);
            // Debug.Log($"poses rank: {poses_array.Rank}");
        }
        else
        {
            Debug.Log("File not found at path: " + npz_file);
        }
        

    }
    void Update()
    {
        
    }
    (ns.NDArray, ns.NDArray,ns.NDArray,ns.NDArray) _load_npz_attribute(string npz_file, 
        string pose_attribute, string shape_attribute, 
        string transl_attribute, string capture_fps_attribute)
    {
        Dictionary<string, object> contents = new Dictionary<string, object>();

        string extract_path = Path.Combine(Application.dataPath, "Dataset");
        string poses_path = Path.Combine(extract_path, "poses.npy");
        string betas_path = Path.Combine(extract_path, "betas.npy");
        string trans_path = Path.Combine(extract_path, "trans.npy");
        string fps_path = Path.Combine(extract_path, "mocap_frame_rate.npy");
        
        if (File.Exists(poses_path))
        {
            File.Delete(poses_path);
            Debug.Log($"Deleted existing file: {poses_path}");
        }
        if (File.Exists(betas_path))
        {
            File.Delete(betas_path);
            Debug.Log($"Deleted existing file: {betas_path}");
        }
        if (File.Exists(trans_path))
        {
            File.Delete(trans_path);
            Debug.Log($"Deleted existing file: {trans_path}");
        }
        if (File.Exists(fps_path))
        {
            File.Delete(fps_path);
            Debug.Log($"Deleted existing file: {fps_path}");
        }

        // Unzip the npz
        using (ZipFile zip = ZipFile.Read(npz_file))
        {
            foreach (ZipEntry entry in zip)
            {
                string name = Path.GetFileNameWithoutExtension(entry.FileName);
                if(name=="poses")
                {
                    Debug.Log($"Extracted file: {pose_attribute}.npy");
                    entry.Extract(extract_path);
                }
                if(name=="betas")
                {
                    Debug.Log($"Extracted file: {shape_attribute}.npy");
                    entry.Extract(extract_path);
                }
                if(name=="trans")
                {
                    Debug.Log($"Extracted file: {transl_attribute}.npy");
                    entry.Extract(extract_path);
                }
                if(name=="mocap_frame_rate")
                {
                    Debug.Log($"Extracted file: {capture_fps_attribute}.npy");
                    entry.Extract(extract_path);
                }
            }
        }    

        // Check if every attribute exsits
        if(!File.Exists(poses_path) ||
        !File.Exists(betas_path) ||
        !File.Exists(trans_path))
        {
            return (null, null, null, null);
        }

        // Every attribute exsits, read them.
        var shapes_NDArray = ns.np.load(betas_path);
        if(shapes_NDArray.ndim == 1)
        {
            single_shape_paramenters = true;
        }
        else
        {
            single_shape_paramenters = false;
        }
        var poses_NDArray = ns.np.load(poses_path);
        var trans_NDArray = ns.np.load(trans_path);
        ns.NDArray fps_NDArray = null;
        if(!File.Exists(fps_path))
        {
            fps_NDArray = ns.np.load(fps_path);
        }

        Debug.Log($"betas: {shapes_NDArray}");
        Debug.Log("betas shape: (" + string.Join(", ", shapes_NDArray.shape)+")");
        Debug.Log($"poses: {poses_NDArray}");
        Debug.Log("poses shape: (" + string.Join(", ", poses_NDArray.shape)+")");
        Debug.Log($"trans: {trans_NDArray}");
        Debug.Log("trans shape: (" + string.Join(", ", trans_NDArray.shape)+")");
        Debug.Log($"fps: {fps_NDArray}");
        if(fps_NDArray!=null)
        {
            Debug.Log("fps shape: (" + string.Join(", ", fps_NDArray.shape)+")");
        }
        
        return (poses_NDArray,shapes_NDArray,trans_NDArray,fps_NDArray);
    }
}
