using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using System.Linq;
using System;
//// Support for reading .npz
using Ionic.Zip;
using np = Numpy;

public class PoseStationTest : MonoBehaviour
{
    private static string relativePath = "../../../dataset/MoSh/50002/jumping_jacks_stageii.npz";
    private bool single_shape_paramenters = true;

    // dict that stores for each body ID the interested body instances
    public Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    
    private string[] npz_files;
    private readonly System.Object locker_initialBodyPositionData = new System.Object();

    // Flag for wheather loaded the target npz file
    public bool load_npz_success = false;
    private Numpy.NDarray poses = Numpy.np.empty(new int[] {0});
    private Numpy.NDarray shapes = Numpy.np.empty(new int[] {0});
    private Numpy.NDarray transls = Numpy.np.empty(new int[] {0});
    private float fps = -1;
    private int num_frames = -1;
    private Vector3 m_initialBodyPositionsData;    
    // private Dictionary<string, Numpy.NDarray> poses_dic = new Dictionary<string, Numpy.NDarray>();
    // private Dictionary<string, Numpy.NDarray> shapes_dic = new Dictionary<string, Numpy.NDarray>();
    // private Dictionary<string, Numpy.NDarray> transls_dic = new Dictionary<string, Numpy.NDarray>();
    // private Dictionary<string, float> fps_dic = new Dictionary<string, float>();
    // private Dictionary<string, int> num_frames_dic = new Dictionary<string, int>();
    // private Dictionary<string, Vector3> m_initialBodyPositionsData_dic = new Dictionary<string, Vector3>();
    
    private int playing_frame_index = 0;
    private int n_shape_components = 10;

    private bool continue_stop = false; // true for continue, false for stop.

    // Start is called before the first frame update
    void Start()
    {        
        string projectRootPath = Application.dataPath;
        string dataset_path_relative = "./Dataset";
        string dataset_path = Path.GetFullPath(Path.Combine(projectRootPath, dataset_path_relative));
        
        // if(Directory.Exists(dataset_path))
        // {
        //     npz_files = Directory.GetFiles(dataset_path, "*.npz", SearchOption.TopDirectoryOnly);
        //     for (int i = 0; i < npz_files.Length; i++)
        //     {
        //         npz_files[i] = Path.GetFileNameWithoutExtension(npz_files[i]);
        //     }
        //     Debug.Log($"read:{string.Join(",",npz_files)}");
        // }

        // The whole path
        string npz_file = Path.GetFullPath(Path.Combine(projectRootPath, relativePath));

        if (File.Exists(npz_file))
        {
                        
            (poses,shapes,transls,fps) = _load_npz_attribute(npz_file, "poses", "betas", "trans", "mocap_frame_rate");
            num_frames = poses.shape[0];
            float[] trans =  get_frame(0,transls);
            lock(locker_initialBodyPositionData)
            {
                m_initialBodyPositionsData = new Vector3(trans[0],trans[2],trans[1]);
            }
            Application.targetFrameRate = (int)fps;

        }
        else
        {
            Debug.Log("File not found at path: " + npz_file);
        }

    }
    void OnDestroy()
    {
        // poses.Dispose();
        // shapes.Dispose();
    }
    void Update()
    {
        // one frame data ready for rendering
        float[] shape = new float[16];

        (float[] pose,float[] trans) = load_one_frame(poses,transls,playing_frame_index);
        trans = _swap_translation_yz_axes_single(trans);
        if(single_shape_paramenters)
        {
            shape = get_shape_single(shapes);
        }
        List<TcpControlledBody> subscribers;
        if(m_registeredBodies.TryGetValue(1, out subscribers))
        {
            Vector3 initBodyPosition;
            lock(locker_initialBodyPositionData)
            {
                initBodyPosition = m_initialBodyPositionsData;
            }
            Vector3 translationDifferenceData = new Vector3(trans[0],trans[1],trans[2]) - initBodyPosition;
            foreach (TcpControlledBody sub in subscribers)
            {
                sub.SetParameters(translationDifferenceData, _add_y_angle_offset_to_pose(_add_x_angle_offset_to_pose(pose,-90),180), _adapt_betas_shape(shape));
            }
        }

        if(continue_stop)
        {
            playing_frame_index++;
        }

        if(playing_frame_index >= num_frames )
        {
            // playing_frame_index = 0;
            continue_stop = false;
        }
    }

    /// @ Load body pose data 
    // Load the npz attributes.
    private (Numpy.NDarray, Numpy.NDarray, Numpy.NDarray, float) _load_npz_attribute(string npz_file, 
        string pose_attribute, string shape_attribute, 
        string transl_attribute, string capture_fps_attribute)
    {
        // Prepare the file paths
        string npz_name = Path.GetFileNameWithoutExtension(npz_file);
        Debug.Log($"npz_name: {npz_name}" );
        string extract_path = Path.Combine(Application.dataPath, "Dataset", npz_name);
        string poses_path = Path.Combine(extract_path, "poses.npy");
        string betas_path = Path.Combine(extract_path, "betas.npy");
        string trans_path = Path.Combine(extract_path, "trans.npy");
        string fps_path = Path.Combine(extract_path, "mocap_frame_rate.npy");
        
        // Update the body pose informations
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
            Debug.Log("Load body attrubutes failed.");
            return (Numpy.np.empty(new int[] {0}), Numpy.np.empty(new int[] {0}), Numpy.np.empty(new int[] {0}), -1);
        }
        else
        {
            load_npz_success = true;
        }

        // Every attribute exsits, read them.
        var shapes_NDArray = np.np.load(betas_path);
        if(shapes_NDArray.ndim == 1)
        {
            single_shape_paramenters = true;
        }
        else
        {
            single_shape_paramenters = false;
        }
        var poses_NDArray = np.np.load(poses_path);
        var trans_NDArray = np.np.load(trans_path);
                
        float fps_value = -1;
        if(File.Exists(fps_path))
        {
            Numpy.NDarray fps_NDArray = np.np.load(fps_path);
            fps_value = fps_NDArray.asscalar<float>();
        }

        // Debug.Log($"betas: {shapes_NDArray}");
        // Debug.Log("betas shape: (" + string.Join(", ", shapes_NDArray.shape)+")");
        // Debug.Log($"poses: {poses_NDArray}");
        // Debug.Log("poses shape: (" + string.Join(", ", poses_NDArray.shape)+")");
        // Debug.Log($"trans: {trans_NDArray}");
        // Debug.Log("trans shape: (" + string.Join(", ", trans_NDArray.shape)+")");
        // Debug.Log($"fps: {fps_NDArray}");
        // if(fps_NDArray.shape.Length == 0)
        // {
        //     Debug.Log("fps shape: (" + string.Join(", ", fps_NDArray.shape)+")");
        // }
        
        return (poses_NDArray,shapes_NDArray,trans_NDArray,fps_value);
    }
    // Read one frame from poses and trans, which ready to align to the 3D model
    private (float[], float[]) load_one_frame(Numpy.NDarray poses, Numpy.NDarray trans, int frame_index)
    {
        // float[] pose_frame = _add_x_angle_offset_to_pose(get_frame(frame_index,poses), -90f);
        float[] pose_frame = get_frame(frame_index,poses);
        // float[] trans_frame = _swap_translation_yz_axes_single(get_frame(frame_index, trans));
        float[] trans_frame = get_frame(frame_index, trans);

        return (pose_frame, trans_frame);
    }
    // Get one frame data of pose/trans
    private float[] get_frame(int frame_index, Numpy.NDarray datas)
    {
        List<float> frame = new List<float>();

        int frame_length = datas.shape[1];
        for(int i=0;i<frame_length; i++)
        {
            frame.Add(datas[frame_index,i].asscalar<float>());
        }
        
        return frame.ToArray();
    }
    private float[] get_shape_single(Numpy.NDarray shapes)
    {
        List<float> datas = new List<float>();
        
        for(int i=0;i<shapes.shape[0];i++)
        {
            datas.Add(shapes[i].asscalar<float>());
        }
        
        return datas.ToArray();
    }
    private float[] _adapt_betas_shape(float[] shape)
    {
        int delta_shape = n_shape_components - shape.Length;
        float[] res = null;

        if(delta_shape == 0)
        {
            res = shape;
        }
        else if(delta_shape > 0)
        {
            for(int i=0;i<delta_shape;i++)
            {
                shape[shape.Length+i]=0;
            }
            res = shape;
        }
        else if(delta_shape < 0){
            res = shape.Take(n_shape_components).ToArray();
        }
        return res;
    }
    private float[] _add_x_angle_offset_to_pose(float[] pose, float x_rot_angle_deg)
    {        
        Vector3 rotation_vector = new Vector3(pose[0], pose[1], pose[2]);
        Quaternion quat = FromRotationVector(rotation_vector);
        Quaternion rotX = FromRotationVector(new Vector3(x_rot_angle_deg*Mathf.Deg2Rad, 0, 0));
        Vector3 rotated = AsRotationVector(rotX * quat);

        pose[0] = rotated.x;
        pose[1] = rotated.y;
        pose[2] = rotated.z;

        return pose;
    }
        float[] _add_y_angle_offset_to_pose(float[] pose, float x_rot_angle_deg)
    {        
        Vector3 rotation_vector = new Vector3(pose[0], pose[1], pose[2]);
        Quaternion quat = FromRotationVector(rotation_vector);
        Quaternion rotX = FromRotationVector(new Vector3(0, x_rot_angle_deg*Mathf.Deg2Rad, 0));
        Vector3 rotated = AsRotationVector(rotX * quat);

        pose[0] = rotated.x;
        pose[1] = rotated.y;
        pose[2] = rotated.z;

        return pose;
    }
    // turn rotation vector into a quaternion.
    private static Quaternion FromRotationVector(Vector3 rotationVector)
    {
        float angle = rotationVector.magnitude;
        if (angle == 0)
        {
            return Quaternion.identity;
        }

        Vector3 axis = rotationVector.normalized;
        float halfAngle = angle / 2.0f;
        float sinHalfAngle = Mathf.Sin(halfAngle);

        return new Quaternion(
            axis.x * sinHalfAngle,
            axis.y * sinHalfAngle,
            axis.z * sinHalfAngle,
            Mathf.Cos(halfAngle)
        );
    }
    // turn a quaternion into a rotation vector
    private static Vector3 AsRotationVector(Quaternion q)
    {
        if (q == Quaternion.identity)
        {
            return Vector3.zero;
        }

        q = q.normalized;
        float angle = 2.0f * Mathf.Acos(q.w);
        float sinHalfAngle = Mathf.Sqrt(1.0f - q.w * q.w);

        if (sinHalfAngle < 0.001f) // 处理极小角度情况
        {
            return new Vector3(q.x, q.y, q.z);
        }
        else
        {
            return new Vector3(q.x / sinHalfAngle * angle, q.y / sinHalfAngle * angle, q.z / sinHalfAngle * angle);
        }
    }
    private float[] _swap_translation_yz_axes_single(float[] trans)
    {
        return new float[] {trans[0], trans[2], trans[1]};
    }

    /// @ Controll of body model
    public bool RegisterBody(GameObject interestedBodyGameObject, int bodyID)
    {
        if (!m_registeredBodies.ContainsKey(bodyID))
        {
            m_registeredBodies.Add(bodyID, new List<TcpControlledBody>());
        }
        TcpControlledBody interestedBody = interestedBodyGameObject.GetComponent<TcpControlledBody>();
        if (interestedBody == null)
            return false;
        if (!m_registeredBodies[bodyID].Contains(interestedBody))
        {
            m_registeredBodies[bodyID].Add(interestedBody);
        }
        return true;
    }
    public bool UnregisterBody(GameObject bodyToUnregisterGameObject, int bodyID)
    {
        if (!m_registeredBodies.ContainsKey(bodyID))
            return false;
        TcpControlledBody bodyToUnregister = bodyToUnregisterGameObject.GetComponent<TcpControlledBody>();
        if (bodyToUnregister == null)
            return false;
        return m_registeredBodies[bodyID].Remove(bodyToUnregister);
    }

    /// @ Replay functions (stop/continue, rewind, fast-forward)
    public bool stop_play()
    {
        continue_stop = false;

        return !continue_stop;
    }
    public bool continue_play()
    {
        continue_stop = true;
        
        return  continue_stop;
    }
    public bool get_continue_stop() { return continue_stop; }

}
