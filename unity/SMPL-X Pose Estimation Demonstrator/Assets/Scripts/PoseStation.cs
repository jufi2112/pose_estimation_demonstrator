using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System;
using Ionic.Zip;
using NumSharp;
using NumSharp.Utilities;

public class PoseStation : MonoBehaviour
{
    private string relativePath = "../../../dataset/MoSh/50002/jumping_jacks_stageii.npz";
    private string dataset_path_relative = "./Dataset";
    private bool single_shape_paramenters = true;

    // dict that stores for each body ID the interested body instances
    Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    
    private List<string> npz_files = new List<string>();
    private readonly System.Object locker_initialBodyPositionData = new System.Object();

    // Flag for wheather loaded the target npz file
    public bool load_npz_success = false;
    private NDArray poses = np.empty(new int[] {0});
    private NDArray shapes = np.empty(new int[] {0});
    private NDArray transls = np.empty(new int[] {0});
    // Hint record_time = num_frames / fps;
    private float fps = -1;
    private int num_frames = -1;
    private Vector3 m_initialBodyPositionsData;    
    private Dictionary<string, NDArray> poses_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, NDArray> shapes_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, NDArray> transls_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, float> fps_dic = new Dictionary<string, float>();
    private Dictionary<string, int> num_frames_dic = new Dictionary<string, int>();
    private Dictionary<string, Vector3> m_initialBodyPositionsData_dic = new Dictionary<string, Vector3>();
    
    private int playing_frame_index = 0;
    private int n_shape_components = 10;
    private float playing_timer = 0.0f;

    /// @ Player controll parameters
    private int boost_rate = 1;
    private bool continue_stop = true; // true for continue, false for stop.
    private bool forward_backward = true; // true for forward, false for backward.
    private Slider progress_slider;

    // Start is called before the first frame update
    void Start()
    {        
        string projectRootPath = Application.dataPath;  
        
        string dataset_path = Path.GetFullPath(Path.Combine(projectRootPath, dataset_path_relative));
        
        if(Directory.Exists(dataset_path))
        {
            string[] npz_files_paths = Directory.GetFiles(dataset_path, "*.npz", SearchOption.TopDirectoryOnly);
            if (npz_files_paths.Length !=0)
            {
                for (int i = 0; i < npz_files_paths.Length; i++)
                {
                    if (File.Exists(npz_files_paths[i]))
                    {
                        npz_files.Add(Path.GetFileNameWithoutExtension(npz_files_paths[i]));
                        (poses_dic[npz_files[i]], shapes_dic[npz_files[i]], transls_dic[npz_files[i]], fps_dic[npz_files[i]])
                            = _load_npz_attribute(npz_files_paths[i], "poses", "betas", "trans", "mocap_frame_rate");
                        num_frames_dic[npz_files[i]] = poses_dic[npz_files[i]].shape[0];
                        float[] trans = get_frame(0, transls_dic[npz_files[i]]);
                        lock (locker_initialBodyPositionData)
                        {
                            m_initialBodyPositionsData_dic[npz_files[i]] = new Vector3(trans[0], trans[2], trans[1]);
                        }
                    }

                }

                poses = poses_dic[npz_files[0]];
                shapes = shapes_dic[npz_files[0]];
                transls = transls_dic[npz_files[0]];
                fps = fps_dic[npz_files[0]];
                num_frames = num_frames_dic[npz_files[0]];
                m_initialBodyPositionsData = m_initialBodyPositionsData_dic[npz_files[0]];
                // Application.targetFrameRate = (int)fps;
                Debug.Log($"total time: {num_frames/fps}");
            }
            else
            {
                Debug.Log("No .npz file found in folder: " + dataset_path);
            }
            
        }
        else
        {
            Debug.Log("Directory not found at path: " + dataset_path);
        }

        // Get Slider
        progress_slider = GameObject.Find("MainProgressSlider").GetComponent<Slider>();
        if (progress_slider == null)
        {
            Debug.Log($"in {this.name} MainProgressSlider not Found!");
        }


    }
    void OnDestroy()
    {
        poses_dic.Clear();
        shapes_dic.Clear();
        transls_dic.Clear();
        fps_dic.Clear();
        num_frames_dic.Clear();
        m_initialBodyPositionsData_dic.Clear();
        m_registeredBodies.Clear();
        poses = null;
        shapes = null;
        transls = null;

    }
    void Update()
    {
        // one frame data ready for rendering
        float[] shape = new float[16];
        if (playing_frame_index == 0)
        {
            playing_timer = 0.0f;
        }
        if (continue_stop)
        {
            playing_timer += Time.deltaTime * boost_rate;
        }

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

        /// @ replaying control logic here.
        if(continue_stop)
        {
            //playing_frame_index += boost_rate;
            playing_frame_index = calculate_frame_index(playing_timer, fps);
            //update_slider(playing_frame_index);
        }

        if (forward_backward) // now forward playing
        {
            if (playing_frame_index >= num_frames - 1)
            {
                playing_frame_index = num_frames-1;
                //update_slider(playing_frame_index);
                continue_stop = false;
            }
            
        }
        else // now backward playing
        {
            if (playing_frame_index <= 0)
            {
                playing_frame_index = 0;
                //update_slider(playing_frame_index);
                forward_backward = true;
                boost_rate = 1;
                continue_stop = false;
            }
        }
    }

    /// @ Load body pose data 

    /// @ Modification of Numsharp Load for single value .npy file
    private static NDArray Load_Scalar_Npy(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open))
            return Load_Scalar_Npy(stream);
    }
    private static NDArray Load_Scalar_Npy(Stream stream)
    {
        using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII
#if !NET35 && !NET40
                , leaveOpen: true
#endif
            ))
        {
            int bytes;
            Type type;
            int[] shape;
            if (!parseReader(reader, out bytes, out type, out shape))
                throw new FormatException();

            Array array;
            if (shape.Length == 0)
            {
                array = Array.CreateInstance(type, 1);
            }
            else
            {
                array = Arrays.Create(type, shape.Aggregate((dims, dim) => dims * dim));
            }

            var result = new NDArray(readValueMatrix(reader, array, bytes, type, shape));
            return result.reshape(shape);
        }
    }
    private static bool parseReader(BinaryReader reader, out int bytes, out Type t, out int[] shape)
    {
        bytes = 0;
        t = null;
        shape = null;

        // The first 6 bytes are a magic string: exactly "x93NUMPY"
        if (reader.ReadChar() != 63) return false;
        if (reader.ReadChar() != 'N') return false;
        if (reader.ReadChar() != 'U') return false;
        if (reader.ReadChar() != 'M') return false;
        if (reader.ReadChar() != 'P') return false;
        if (reader.ReadChar() != 'Y') return false;

        byte major = reader.ReadByte(); // 1
        byte minor = reader.ReadByte(); // 0

        if (major != 1 || minor != 0)
            throw new NotSupportedException();

        ushort len = reader.ReadUInt16();

        string header = new String(reader.ReadChars(len));
        string mark = "'descr': '";
        int s = header.IndexOf(mark) + mark.Length;
        int e = header.IndexOf("'", s + 1);
        string type = header.Substring(s, e - s);
        bool? isLittleEndian;
        t = GetType(type, out bytes, out isLittleEndian);

        if (isLittleEndian.HasValue && isLittleEndian.Value == false)
            throw new Exception();

        mark = "'fortran_order': ";
        s = header.IndexOf(mark) + mark.Length;
        e = header.IndexOf(",", s + 1);
        bool fortran = bool.Parse(header.Substring(s, e - s));

        if (fortran)
            throw new Exception();

        mark = "'shape': (";
        s = header.IndexOf(mark) + mark.Length;
        e = header.IndexOf(")", s + 1);
        if (e > 0)
        {
            shape = header.Substring(s, e - s).Split(',').Where(v => !String.IsNullOrEmpty(v)).Select(Int32.Parse).ToArray();
        }
        else
        {
            shape = new int[0];
        }

        return true;
    }
    private static Array readValueMatrix(BinaryReader reader, Array matrix, int bytes, Type type, int[] shape)
    {
        int total = 1;
        for (int i = 0; i < shape.Length; i++)
            total *= shape[i];
        var buffer = new byte[bytes * total];

        reader.Read(buffer, 0, buffer.Length);
        Buffer.BlockCopy(buffer, 0, matrix, 0, buffer.Length);

        return matrix;
    }
    private static Type GetType(string dtype, out int bytes, out bool? isLittleEndian)
    {
        isLittleEndian = IsLittleEndian(dtype);
        bytes = Int32.Parse(dtype.Substring(2));

        string typeCode = dtype.Substring(1);

        if (typeCode == "b1")
            return typeof(bool);
        if (typeCode == "i1")
            return typeof(Byte);
        if (typeCode == "i2")
            return typeof(Int16);
        if (typeCode == "i4")
            return typeof(Int32);
        if (typeCode == "i8")
            return typeof(Int64);
        if (typeCode == "u1")
            return typeof(Byte);
        if (typeCode == "u2")
            return typeof(UInt16);
        if (typeCode == "u4")
            return typeof(UInt32);
        if (typeCode == "u8")
            return typeof(UInt64);
        if (typeCode == "f4")
            return typeof(Single);
        if (typeCode == "f8")
            return typeof(Double);
        if (typeCode.StartsWith("S"))
            return typeof(String);

        throw new NotSupportedException();
    }
    private static bool? IsLittleEndian(string type)
    {
        bool? littleEndian = null;

        switch (type[0])
        {
            case '<':
                littleEndian = true;
                break;
            case '>':
                littleEndian = false;
                break;
            case '|':
                littleEndian = null;
                break;
            default:
                throw new Exception();
        }

        return littleEndian;
    }

    // Load the npz attributes.
    private (NDArray, NDArray, NDArray, float) _load_npz_attribute(string npz_file, 
        string pose_attribute, string shape_attribute, 
        string transl_attribute, string capture_fps_attribute)
    {
        // Prepare the file paths
        string npz_name = Path.GetFileNameWithoutExtension(npz_file);
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
            return (np.empty(new int[] {0}), np.empty(new int[] {0}), np.empty(new int[] {0}), -1);
        }
        else
        {
            load_npz_success = true;
        }

        // Every attribute exsits, read them.
        var shapes_NDArray = np.load(betas_path);
        if(shapes_NDArray.ndim == 1)
        {
            single_shape_paramenters = true;
        }
        else
        {
            single_shape_paramenters = false;
        }
        var poses_NDArray = np.load(poses_path);
        var trans_NDArray = np.load(trans_path);

        float fps_value = -1;
        if(File.Exists(fps_path))
        {
            NDArray fps_NDArray = Load_Scalar_Npy(fps_path);
            fps_value = np.asscalar<float>(fps_NDArray);
        }
        
        return (poses_NDArray,shapes_NDArray,trans_NDArray,fps_value);
    }
    // Read one frame from poses and trans, which ready to align to the 3D model
    private (float[], float[]) load_one_frame(NDArray poses, NDArray trans, int frame_index)
    {
        // float[] pose_frame = _add_x_angle_offset_to_pose(get_frame(frame_index,poses), -90f);
        float[] pose_frame = get_frame(frame_index,poses);
        // float[] trans_frame = _swap_translation_yz_axes_single(get_frame(frame_index, trans));
        float[] trans_frame = get_frame(frame_index, trans);

        return (pose_frame, trans_frame);
    }
    // Get one frame data of pose/trans
    private float[] get_frame(int frame_index, NDArray datas)
    {
        List<float> frame = new List<float>();

        int frame_length = datas.shape[1];
        for(int i=0;i<frame_length; i++)
        {
            frame.Add(np.asscalar<float>(datas[frame_index, i]));
            // frame.Add(datas[frame_index,i].asscalar<float>()); // using Numpy.net
        }
        
        return frame.ToArray();
    }
    private float[] get_shape_single(NDArray shapes)
    {
        List<float> datas = new List<float>();
        
        for(int i=0;i<shapes.shape[0];i++)
        {
            datas.Add(np.asscalar<float>(shapes[i]));
            // datas.Add(shapes[i].asscalar<float>()); // using Numpy.net
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
    private float[] _add_y_angle_offset_to_pose(float[] pose, float y_rot_angle_deg)
    {        
        Vector3 rotation_vector = new Vector3(pose[0], pose[1], pose[2]);
        Quaternion quat = FromRotationVector(rotation_vector);
        Quaternion rotY = FromRotationVector(new Vector3(0, y_rot_angle_deg*Mathf.Deg2Rad, 0));
        Vector3 rotated = AsRotationVector(rotY * quat);

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
    public void ContinueStop()
    {   
        bool previous_play_state = continue_stop;
        continue_stop = !continue_stop;
    }
    public bool get_continue_stop() { return continue_stop; }
    public void Rewind()
    {
        if(boost_rate > 1)
        {
            boost_rate = 1;
            forward_backward = true;
        }
        else if(boost_rate == 1)
        {
            boost_rate = -1;
            forward_backward = false;
        }
        else if (boost_rate >= -8 && boost_rate < 0)
        {
            boost_rate--;
            forward_backward = false;
        }
    }
    public void FastForward()
    {
        if(boost_rate < -1)
        {
            boost_rate = -1;
            forward_backward = false;
        }
        else if (boost_rate == -1)
        {
            boost_rate = 1;
            forward_backward = true;
        }
        else if (boost_rate <= 8 && boost_rate > 0)
        {
            boost_rate++;
            forward_backward = true;
        }
    }
    private int calculate_frame_index(float timer, float frame_rate)
    {
        return (int)Math.Floor(timer * frame_rate);
    }
    public void pause()
    {
        continue_stop = false;
    }
    public void resume(bool last_play_state)
    {
        continue_stop = last_play_state;
    }
    public void set_playing_frame_index(int index)
    {
        playing_frame_index = index;
        playing_timer = index / fps;
    }
    /// @ Functions for outer use
    public int get_num_frames()
    {
        return num_frames;
    }
    public float get_fps()
    {
        return fps;
    }
    public int get_playing_frame_index() { return playing_frame_index; }
    public void update_slider(int playing_frame_index)
    {
        progress_slider.value = playing_frame_index / fps;
    }

    public void update_play_frame(int new_frame_index)
    {
        playing_frame_index = new_frame_index;
        playing_timer = new_frame_index / fps;
    }


}
