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
using NumSharp;
//using Numpy;

public class PoseStation : MonoBehaviour
{
    private string relativePath = "../../../dataset/MoSh/50002/jumping_jacks_stageii.npz";
    private bool single_shape_paramenters = true;
    private float[] poses_array;
    private float[] shapes_array;
    private float[] transl_array;
    private int? mocap_fps = null;
    // Start is called before the first frame update
    void Start()
    {
        string projectRootPath = Application.dataPath;

        // The whole path
        string npz_file = Path.GetFullPath(Path.Combine(projectRootPath, relativePath));

        if (File.Exists(npz_file))
        {
           (poses_array,shapes_array,transl_array,mocap_fps) = _load_npz_attribute(npz_file, "poses", "betas", "trans", "mocap_frame_rate");
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

    // Update is called once per frame
    void Update()
    {
        
    }


    object ParseNpyFile(byte[] fileBytes)
    {
        // Read the header
        int headerLength = BitConverter.ToInt16(fileBytes, 8);
        string header = Encoding.ASCII.GetString(fileBytes, 10, headerLength);
        // Debug.Log("header: " + header);

        // Determine data type from header
        if (header.Contains("'descr': '|S"))
        {
            // String data
            int dataStart = 10 + headerLength;
            string result = Encoding.ASCII.GetString(fileBytes, dataStart, fileBytes.Length - dataStart).TrimEnd('\0');
            return result;
        }
        else if (header.Contains("'descr': '<f"))
        {
            // Float data
            int dataStart = 10 + headerLength;
            int numFloats = (fileBytes.Length - dataStart) / 4;
            float[] array = new float[numFloats];
            for (int i = 0; i < numFloats; i++)
            {
                array[i] = BitConverter.ToSingle(fileBytes, dataStart + i * 4);
            }
            return array;
        }
        else if (header.Contains("'descr': '<i"))
        {
            // Integer data
            int dataStart = 10 + headerLength;
            int numInts = (fileBytes.Length - dataStart) / 4;
            int[] array = new int[numInts];
            for (int i = 0; i < numInts; i++)
            {
                array[i] = BitConverter.ToInt32(fileBytes, dataStart + i * 4);
            }
            return array;
        }
        // Add other data type parsing as needed
        // e.g., int, double, etc.

        return null;
    }

    (float[], float[],float[],int?) _load_npz_attribute(string npz_file, 
        string pose_attribute, string shape_attribute, 
        string transl_attribute, string capture_fps_attribute)
    {
        Dictionary<string, object> contents = new Dictionary<string, object>();

        
        // Unzip the npz
        using (ZipFile zip = ZipFile.Read(npz_file))
        {
            foreach (ZipEntry entry in zip)
            {
                string name = Path.GetFileNameWithoutExtension(entry.FileName);
                if(name=="poses")
                {
                    string poses_path = Path.Combine(Application.dataPath, "Dataset", "poses.npy");
                    entry.Extract(poses_path);
                }
                if(name=="betas")
                {
                    string betas_path = Path.Combine(Application.dataPath, "Dataset", "betas.npy");
                    entry.Extract(betas_path);
                }
                if(name=="trans")
                {
                    string trans_path = Path.Combine(Application.dataPath, "Dataset", "trans.npy");
                    entry.Extract(trans_path);
                }
                if(name=="mocap_frame_rate")
                {
                    string fps_path = Path.Combine(Application.dataPath, "Dataset", "mocap_frame_rate.npy");
                    entry.Extract(fps_path);
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    entry.Extract(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    byte[] npz_bytes = ms.ToArray();

                    // string name = Path.GetFileNameWithoutExtension(entry.FileName);
                    if(name == "betas")
                    {
                        Debug.Log(entry);
                    }
                    object value = ParseNpyFile(npz_bytes);
                    contents[name] = value;
                }
            }
        }    

        // Check if every attribute exsits
        if (!contents.ContainsKey(pose_attribute) ||
        !contents.ContainsKey(shape_attribute) ||
        !contents.ContainsKey(transl_attribute))
        {
            return (null,null,null,null);
        }
        
        // every target exsits, slice them out.
        var shapes = contents[shape_attribute] as float[];
        var shapes1 = contents[shape_attribute] as double[];
        if (shapes.Rank == 1)
        {
            single_shape_paramenters = true;
        }
        else
        {
            single_shape_paramenters = false;
        }
        var poses = contents[pose_attribute] as float[];
        var transl = contents[transl_attribute] as float[];
        object fps_obj;
        if(!contents.TryGetValue(capture_fps_attribute, out fps_obj))
        {
            fps_obj = null;
        }
        int? fps = fps_obj as int?;
        var fpss = contents[capture_fps_attribute] as int[];

        // Debug.Log("shape l: " + shapes.Length);
        // Debug.Log(string.Join(", ", shapes));
        // Debug.Log("fps: " + fpss.Length);
        // Debug.Log(string.Join(", ", fpss));

        
        return (poses,shapes,transl,fps);
    }
    /*
    private void _load_npz_attribute(string npz_file)
    {
        
        if (!File.Exists(npz_file))
        {
            Debug.LogError("Error: Not a valid file: " + npz_file);
            //return (null, null, null, null);
        }

        Dictionary<string, object> contents = new Dictionary<string, object>;
        // var content = np.load(npz_file);
        //var contents = np.load(npz_file);
        var contents = LoadNPZFile(npz_file);
        foreach(var key in contents.Keys)
        {
            Debug.Log(key+": " + contents[key]);
        }

        //NDArray test = contents[0];
        //Debug.Log("npz content: " + test);


        // if (!contents.ContainsKey("poses.npy"))
        // {
        //     Debug.LogError("Could not find pose attribute in " + npz_file);
        //     return;
        // }
        // if (!contents.ContainsKey("betas.npy"))
        // {
        //     Debug.LogError("Could not find shape attribute in " + npz_file);
        //     return;
        // }
        // if (!contents.ContainsKey("trans"))
        // {
        //     Debug.LogError("Could not find transl attribute in " + npz_file);
        //     return;
        // }

        // var shapeArray = contents["betas.npy"];
        // Debug.Log("shapeArray: " + shapeArray);
        // if( shapeArray != null)
        // {
        //     Debug.Log("Size of betasArray: " + shapeArray.Length);
        //     foreach (var content in shapeArray)
        //     {
        //         Debug.Log("poses:" + content);
        //     }
        // }

        // foreach(object content in contents["betas.npy"])
        // {
        //     Debug.Log("betas:" + content);
        // }

        //return (null, null, null, null);
    }
    */
}
