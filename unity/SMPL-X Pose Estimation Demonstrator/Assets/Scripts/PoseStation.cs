using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
using SN = System.Numerics;
using Ionic.Zip;
using NumSharp;
using NumSharp.Utilities;

public class PoseStation : MonoBehaviour
{
    //private string relativePath = "../../../dataset/MoSh/50002/jumping_jacks_stageii.npz";
    private string dataset_path_relative = "./Dataset";
    private bool single_shape_paramenters = true;

    // dict that stores for each body ID the interested body instances
    Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    
    private List<string> npz_files = new List<string>();
    private readonly System.Object locker_initialBodyPositionData = new System.Object();
        
    public bool load_npz_success = false;
    
    /// @ Copy buffer
    private NDArray copied_poses = np.empty(new int[] {0});
    private NDArray copied_shapes = np.empty(new int[] {0});
    private NDArray copied_transls = np.empty(new int[] {0});
    private float copied_fps = -1;
    private int copied_num_frames = -1;
    /// @ Load .npz file buffer
    private Dictionary<string, NDArray> poses_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, NDArray> shapes_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, NDArray> transls_dic = new Dictionary<string, NDArray>();
    private Dictionary<string, float> fps_dic = new Dictionary<string, float>();
    private Dictionary<string, int[]> num_frames_dic = new Dictionary<string, int[]>();
    private Dictionary<string, Vector3[]> initialBodyPosition_dic = new Dictionary<string, Vector3[]>();
    /// @ Playing Infos
    private int playing_frame_index = -1;
    private float playing_timer = 0.0f;
    private int playing_file_index = 0;
    private int playing_body_id = 0;

    /// @ Model Settings
    private int n_shape_components = 10;

    /// @ Player controll parameters
    private float boost_rate = 1;
    private bool continue_stop = true; // true for continue, false for stop.
    private bool forward_backward = true; // true for forward, false for backward.
    private Slider progress_slider;
    private Dropdown boost_rate_dropdown;

    // Start is called before the first frame update
    void Start()
    {        
        string projectRootPath = Application.dataPath;  
        
        string dataset_path = Path.GetFullPath(Path.Combine(projectRootPath, dataset_path_relative));
        
        if(Directory.Exists(dataset_path))
        {
            string[] npz_files_paths = Directory.GetFiles(dataset_path, "*.npz", SearchOption.TopDirectoryOnly).Where(file => !file.EndsWith(".meta")).ToArray();

            if (npz_files_paths.Length !=0)
            {
                for (int i = 0; i < npz_files_paths.Length; i++)
                {
                    if (File.Exists(npz_files_paths[i]))
                    {
                        npz_files.Add(Path.GetFileNameWithoutExtension(npz_files_paths[i])); // Add into file list

                        // Load poses.npy betas.npy and transls.npy from xxx.npz  with fileindex i.
                        (poses_dic[npz_files[i]], shapes_dic[npz_files[i]], transls_dic[npz_files[i]], fps_dic[npz_files[i]])
                            = _load_npz_attribute(npz_files_paths[i], "poses", "betas", "trans", "mocap_frame_rate"); // load .npy from .npz into NDArray.

                        if (shapes_dic[npz_files[i]].ndim == 1)
                        {
                            initialBodyPosition_dic[npz_files[i]] = compute_initial_trans(transls_dic[npz_files[i]], true);
                            num_frames_dic[npz_files[i]] = new int[] { poses_dic[npz_files[i]].shape[0] };
                        }
                        else
                        {
                            for (int j=0; j<shapes_dic[npz_files[i]].shape[0];j++)
                            {
                                initialBodyPosition_dic[npz_files[i]] = compute_initial_trans(transls_dic[npz_files[i]], false);
                                num_frames_dic[npz_files[i]][j] = poses_dic[npz_files[i]][j].shape[0];
                            }
                        }
                        //num_frames_dic[npz_files[i]] = poses_dic[npz_files[i]].shape[0];
                        //float[] trans = get_frame(0, transls_dic[npz_files[i]]);
                        //m_initialBodyPositionsData_dic[npz_files[i]] = new Vector3(trans[0], trans[2], trans[1]);
                    }
                }

                //shapes = shapes_dic[npz_files[playing_file_index]];
                if (shapes_dic[npz_files[playing_file_index]].ndim == 1)
                {
                    single_shape_paramenters = true;
                }
                else
                {
                    single_shape_paramenters = false;
                }
                //poses = poses_dic[npz_files[playing_file_index]];
                //transls = transls_dic[npz_files[playing_file_index]];
                //fps = fps_dic[npz_files[playing_file_index]];
                //num_frames = compute_num_frames(poses, single_shape_paramenters);
                //m_initialBodyPositionsData = compute_initial_trans(transls, single_shape_paramenters); 
                foreach(var file in npz_files)
                {
                    var orientation = poses_dic[file][0][$":{3}"];
                    Debug.Log($"{file}: {orientation}");
                }

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

        // Get Main Progress Slider
        progress_slider = GameObject.Find("MainProgressSlider").GetComponent<Slider>();
        if (progress_slider == null)
        {
            Debug.Log($"in {this.name} MainProgressSlider not Found!");
        }

        // Get Boost Rate Dropdown
        boost_rate_dropdown = GameObject.Find("BoostRateDropdown").GetComponent<Dropdown>();
        if (progress_slider == null)
        {
            Debug.Log($"in {this.name} BoostRateDropdown not Found!");
        }

    }
    void OnDestroy()
    {
        poses_dic.Clear();
        shapes_dic.Clear();
        transls_dic.Clear();
        fps_dic.Clear();
        m_registeredBodies.Clear();

    }
    void Update()
    {
        if (playing_frame_index == -1)
        {
            playing_timer = 0.0f;
        }

        /// @ replaying control logic here.
        if (continue_stop)
        {
            if (forward_backward)
            {
                playing_timer += Time.deltaTime * boost_rate;
            }
            else
            {
                playing_timer -= Time.deltaTime * boost_rate;
            }
            playing_frame_index = calculate_frame_index(playing_timer, fps_dic[npz_files[playing_file_index]]);
            if (forward_backward) 
            {
                // now forward played to end
                if (playing_frame_index > num_frames_dic[npz_files[playing_file_index]][playing_body_id] - 1)
                {
                    playing_frame_index = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - 1;
                    playing_timer = num_frames_dic[npz_files[playing_file_index]][playing_body_id] / fps_dic[npz_files[playing_file_index]];
                    boost_rate = 1;
                    boost_rate_dropdown.value = 2;
                    continue_stop = false;
                }
            }
            else
            {
                // now backward played to start
                if (playing_frame_index < 0)
                {
                    playing_frame_index = 0;
                    //update_slider(playing_frame_index);
                    forward_backward = true;
                    boost_rate = 1;
                    boost_rate_dropdown.value = 2;
                    continue_stop = false;
                }
            }
        }
        
        if (single_shape_paramenters)
        {
            // Load one frame data for rendering
            float[] shape = new float[16];
            //(float[] pose, float[] trans) = load_one_frame(poses, transls, playing_frame_index);
            (float[] pose, float[] trans) = load_one_frame(poses_dic[npz_files[playing_file_index]], transls_dic[npz_files[playing_file_index]], playing_frame_index);

            trans = _swap_translation_yz_axes_single(trans);
            if (single_shape_paramenters)
            {
                shape = get_shape_single(shapes_dic[npz_files[playing_file_index]]);
            }
            List<TcpControlledBody> subscribers;
            if (m_registeredBodies.TryGetValue(1, out subscribers))
            {
                Vector3 initBodyPosition;
                lock (locker_initialBodyPositionData)
                {
                    initBodyPosition = initialBodyPosition_dic[npz_files[playing_file_index]][playing_body_id];
                    //initBodyPosition = m_initialBodyPositionsData[0];
                }
                //trans = _add_x_angle_offset_to_pose(trans, -90);
                Vector3 translationDifferenceData = new Vector3(trans[0], trans[1], trans[2]) - initBodyPosition;
                foreach (TcpControlledBody sub in subscribers)
                {
                    if(continue_stop)
                    {
                        Debug.Log($"pose pelvis - {playing_frame_index} : [{pose[0]}, {pose[1]}, {pose[2]}");
                    }
                    //sub.SetParameters(translationDifferenceData, _add_y_angle_offset_to_pose(_add_x_angle_offset_to_pose(pose, -90), 180), _adapt_betas_shape(shape));
                    //sub.SetParameters(translationDifferenceData, _add_x_angle_offset_to_pose(pose, -90), _adapt_betas_shape(shape));

                    sub.SetParameters(translationDifferenceData, pose, _adapt_betas_shape(shape));
                }
            }
        }
        else
        {
            for (int i=0;i< shapes_dic[npz_files[playing_file_index]].shape[0];i++)
            {
                // Load one frame data for rendering
                float[] shape = new float[16];
                //(float[] pose, float[] trans) = load_one_frame(poses[i], transls[i], playing_frame_index);
                (float[] pose, float[] trans) = load_one_frame(poses_dic[npz_files[playing_file_index]], transls_dic[npz_files[playing_file_index]], playing_frame_index);

                trans = _swap_translation_yz_axes_single(trans);
                shape = get_shape_single(shapes_dic[npz_files[playing_file_index]][i]);
               
                List<TcpControlledBody> subscribers;
                if (m_registeredBodies.TryGetValue(i+1, out subscribers))
                {
                    Vector3 initBodyPosition;
                    lock (locker_initialBodyPositionData)
                    {
                        initBodyPosition = initialBodyPosition_dic[npz_files[playing_file_index]][playing_body_id];
                        //initBodyPosition = m_initialBodyPositionsData[i];
                    }
                    Vector3 translationDifferenceData = new Vector3(trans[0], trans[1], trans[2]) - initBodyPosition;
                    foreach (TcpControlledBody sub in subscribers)
                    {
                        //sub.SetParameters(translationDifferenceData, _add_y_angle_offset_to_pose(_add_x_angle_offset_to_pose(pose, -90), 180), _adapt_betas_shape(shape));
                        sub.SetParameters(translationDifferenceData, _add_x_angle_offset_to_pose(pose, -90), _adapt_betas_shape(shape));
                    }
                }
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
    
    // Compute num_frames
    private static int[] compute_num_frames(NDArray poses, bool single_shape_parameters)
    {
        List<int> frames = new List<int>();
       if (single_shape_parameters) // Single body
        {
            frames.Add(poses.shape[0]);
        }
       else
        {
            for (int i=0; i<poses.shape[0];i++)
            {
                frames.Add(poses[i].shape[0]);
            }
        }

        return frames.ToArray();
    }
    private static Vector3[] compute_initial_trans(NDArray transls, bool single_shape_parameters)
    {
        List<Vector3> init_trans = new List<Vector3>();
        if (single_shape_parameters)
        {
            Vector3 _trans = new Vector3(np.asscalar<float>(transls[0, 0]), np.asscalar<float>(transls[0, 2]), np.asscalar<float>(transls[0, 1]));
            init_trans.Add(_trans);
        }
        else
        {
            for (int i=0; i<transls.shape[0];i++)
            {
                Vector3 _trans = new Vector3(np.asscalar<float>(transls[i][0, 0]), np.asscalar<float>(transls[i][0, 2]), np.asscalar<float>(transls[i][0, 1]));
                init_trans.Add(_trans);
            }
        }

        return init_trans.ToArray();
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
        if(boost_rate > 1 && forward_backward == true)
        {
            boost_rate = 1;
            forward_backward = true;
        }
        else if(boost_rate == 1 && forward_backward == true)
        {
            forward_backward = false;
        }
        else if (boost_rate == 0.25 && forward_backward == false)
        {
            boost_rate = 1;
        }
        else if (boost_rate == 0.5 && forward_backward == false)
        {
            boost_rate = 1;
        }
        else if (boost_rate == 1 && forward_backward == false)
        {
            boost_rate++;
        }
        else if (boost_rate == 1.5 && forward_backward == false)
        {
            boost_rate = 2;
        }
        else if (boost_rate <= 8 && boost_rate > 1.5 && forward_backward == false)
        {
            boost_rate++;
            forward_backward = false;
        }

        // Update Boost Rate Dropdown
        if (boost_rate == 1)
        {
            boost_rate_dropdown.value = 2;
        }
        else
        {
            boost_rate_dropdown.value = (int)boost_rate + 2;
        }
    }
    public void FastForward()
    {
        if(boost_rate > 1 && forward_backward == false)
        {
            boost_rate = 1;
            forward_backward = false;
        }
        else if (boost_rate == 1 && forward_backward == false)
        {
            forward_backward = true;
        }
        else if (boost_rate == 0.25 && forward_backward == true)
        {
            boost_rate = 1;
        }
        else if (boost_rate == 0.5 && forward_backward == true)
        {
            boost_rate = 1;
        }
        else if (boost_rate == 1 && forward_backward == true)
        {
            boost_rate++;
        }
        else if (boost_rate == 1.5 && forward_backward == true)
        {
            boost_rate = 2;
        }
        else if (boost_rate <= 8 && boost_rate > 1.5 && forward_backward == true)
        {
            boost_rate++;
            forward_backward = true;
        }

        // Update Boost Rate Dropdown
        if (boost_rate == 1)
        {
            boost_rate_dropdown.value = 2;
        }
        else
        {
            boost_rate_dropdown.value = (int)boost_rate + 2;
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
    public void set_playing_timer(float timer)
    {
        playing_timer = timer;
        playing_frame_index = (int)(timer * fps_dic[npz_files[playing_file_index]]);
    }
    public void set_boost_rate(float new_boost_rate)
    {
        boost_rate = new_boost_rate;
    }
    public float get_booste_rate() { return boost_rate; }

    public int get_num_body() { return single_shape_paramenters ? 1 : shapes_dic[npz_files[playing_file_index]].shape[0]; }
    public int get_playing_body_id() { return playing_body_id; }
    public void set_playing_body_id(int new_body_id) { playing_body_id = new_body_id; }
    /// @ Functions for File choose
    public List<string> get_npz_files() {  return npz_files; }
    public void change_file(int file_index)
    {
        if(file_index != playing_file_index)
        {
            // update playing_file_index
            playing_file_index = file_index;

            // Init playing state
            playing_frame_index = -1;
            continue_stop = false;
            forward_backward = true;
            playing_body_id = 0;

            // Init playing data
            //shapes = shapes_dic[npz_files[playing_file_index]];
            if (shapes_dic[npz_files[playing_file_index]].ndim == 1)
            {
                single_shape_paramenters = true;
            }
            else
            {
                single_shape_paramenters = false;
            }
            //poses = poses_dic[npz_files[playing_file_index]];
            //transls = transls_dic[npz_files[playing_file_index]];
            //fps = fps_dic[npz_files[playing_file_index]];
            //num_frames = compute_num_frames(poses,single_shape_paramenters);
            //m_initialBodyPositionsData = compute_initial_trans(transls, single_shape_paramenters);
        }
    }

    /// @ Functions for outer use
    public int get_num_frames(int body_id )
    {
        return num_frames_dic[npz_files[playing_file_index]][body_id];
    }
    public float get_fps()
    {
        return fps_dic[npz_files[playing_file_index]];
    }
    public int get_playing_frame_index() { return playing_frame_index; }
    public void update_slider(int playing_frame_index)
    {
        progress_slider.value = playing_frame_index / fps_dic[npz_files[playing_file_index]];
    }
    public void update_play_frame(int new_frame_index)
    {
        playing_frame_index = new_frame_index;
        playing_timer = new_frame_index / fps_dic[npz_files[playing_file_index]];
    }
    public string get_playing_filename()
    {
        return npz_files[playing_file_index];
    }

    /// @ Editor Functions
    public void copy_slice(float start_time, float end_time, bool copy_shape)
    {
        // 
        copied_fps = -1;
        copied_num_frames = -1;
        copied_poses = np.empty(new int[] { 0 });
        copied_shapes = np.empty(new int[] { 0 });
        copied_transls = np.empty(new int[] { 0 });

        copied_fps = fps_dic[npz_files[playing_file_index]];
        int start_index = calculate_frame_index(start_time, fps_dic[npz_files[playing_file_index]]);
        int end_index = calculate_frame_index(end_time, fps_dic[npz_files[playing_file_index]]);
        copied_num_frames = end_index - start_index + 1;
        if (single_shape_paramenters)
        {
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]].Clone(); }
            copied_poses = poses_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].Clone();
            copied_transls = transls_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].Clone();
            copied_fps = fps_dic[npz_files[playing_file_index]];
        }
        else
        {
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]][playing_body_id].Clone(); }
            copied_poses = poses_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"].Clone();
            copied_transls = transls_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"].Clone();
            copied_fps = fps_dic[npz_files[playing_file_index]];
        }

    }
    public void cut_slice(float start_time, float end_time, bool copy_shape)
    {
        // 
        copied_fps = -1;
        copied_num_frames = -1;
        copied_poses = np.empty(new int[] { 0 });
        copied_shapes = np.empty(new int[] { 0 });
        copied_transls = np.empty(new int[] { 0 });

        copied_fps = fps_dic[npz_files[playing_file_index]];
        int start_index = calculate_frame_index(start_time, fps_dic[npz_files[playing_file_index]]);
        int end_index = calculate_frame_index(end_time, fps_dic[npz_files[playing_file_index]]);
        copied_num_frames = end_index - start_index + 1;

        if (single_shape_paramenters)
        {
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]].Clone(); }
            copied_poses = poses_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].Clone();
            copied_transls = transls_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].Clone();
            copied_fps = fps_dic[npz_files[playing_file_index]];

            // Apply edit to playing cache
            num_frames_dic[npz_files[playing_file_index]][playing_body_id] -= copied_num_frames;
            poses_dic[npz_files[playing_file_index]] = DeleteRange(poses_dic[npz_files[playing_file_index]], start_index, end_index);
            transls_dic[npz_files[playing_file_index]] = DeleteRange(transls_dic[npz_files[playing_file_index]], start_index, end_index);
        }
        else
        {
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]][playing_body_id]; }
            copied_poses = poses_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"];
            copied_transls = transls_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"];
            copied_fps = fps_dic[npz_files[playing_file_index]];    
        }
    }
    public void replace_slice(int replace_start_index, int replace_end_index, bool copy_shape)
    {
        if (copied_fps != fps_dic[npz_files[playing_file_index]])
        {
            copied_poses = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_poses);
            copied_transls = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_transls);
        }

        if (single_shape_paramenters)
        {
            if (copy_shape)
            {
                //shapes = copied_shapes;
                shapes_dic[npz_files[playing_file_index]] = copied_shapes;
            }

            num_frames_dic[npz_files[playing_file_index]][playing_body_id] = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - (replace_end_index - replace_start_index + 1) + copied_num_frames;
            poses_dic[npz_files[playing_file_index]] = Insert2DArray(DeleteRange(poses_dic[npz_files[playing_file_index]], replace_start_index, replace_end_index), copied_poses, replace_start_index - 1, false);
            transls_dic[npz_files[playing_file_index]] = Insert2DArray(DeleteRange(transls_dic[npz_files[playing_file_index]], replace_start_index, replace_end_index), copied_transls, replace_start_index - 1, true);
        }
        else
        {
            if (copy_shape)
            {
                shapes_dic[npz_files[playing_file_index]] = copied_shapes;
            }

            num_frames_dic[npz_files[playing_file_index]][playing_body_id] = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - (replace_end_index - replace_start_index + 1) + copied_num_frames;
            poses_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(DeleteRange(poses_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_poses, replace_start_index - 1, false);
            transls_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(DeleteRange(transls_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_transls, replace_start_index - 1, true);

        }
    }
    public void paste_slice(int insert_index, bool copy_shape)
    {
        if (copied_fps != fps_dic[npz_files[playing_file_index]])
        {
            copied_poses = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_poses);
            copied_transls = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_transls);
        }

        if (single_shape_paramenters)
        {
            if (copy_shape)
            {
                //shapes = copied_shapes;
                //shapes_dic[npz_files[playing_file_index]] = copied_shapes;
                
            }
/*            var orientation_difference = poses_dic[npz_files[playing_file_index]][insert_index][$":{3}"] - copied_poses[0][$":{3}"];
            SN.Matrix4x4 rotX = SN.Matrix4x4.CreateRotationX(np.asscalar<float>(orientation_difference[0]));
            SN.Matrix4x4 rotY = SN.Matrix4x4.CreateRotationY(np.asscalar<float>(orientation_difference[1]));
            SN.Matrix4x4 rotZ = SN.Matrix4x4.CreateRotationZ(np.asscalar<float>(orientation_difference[2]));
            SN.Matrix4x4 rotationMatrix = rotZ * rotY * rotX;

            for (int i = 0; i < copied_transls.shape[1]; i++)
            {
                SN.Vector3 trans = new SN.Vector3(np.asscalar<float>(copied_transls[i, 0]), np.asscalar<float>(copied_transls[i, 1]), np.asscalar<float>(copied_transls[i, 2]));
                SN.Vector3 rotated_trans = SN.Vector3.Transform(trans, rotationMatrix);
                Debug.Log("--------------------------------------------------------------");
                Debug.Log($"diff:  {orientation_difference}");
                Debug.Log($"trans: {trans}");
                Debug.Log($"rotat: {rotated_trans}");
                Debug.Log("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
                float[] rotat = new float[] { rotated_trans.X, rotated_trans.Y, rotated_trans.Z };
                copied_transls[i] = np.array(rotat);
            }*/

            num_frames_dic[npz_files[playing_file_index]][playing_body_id] += copied_num_frames;
            poses_dic[npz_files[playing_file_index]] = Insert2DArray(poses_dic[npz_files[playing_file_index]], copied_poses, insert_index, false, true);
            transls_dic[npz_files[playing_file_index]] = Insert2DArray(transls_dic[npz_files[playing_file_index]], copied_transls, insert_index, true, false);
        }
        else
        {
            if (copy_shape)
            {
                //shapes[playing_body_id] = copied_shapes;
                shapes_dic[npz_files[playing_file_index]][playing_body_id] = copied_shapes;
            }

            num_frames_dic[npz_files[playing_file_index]][playing_body_id] += copied_num_frames;
            poses_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(poses_dic[npz_files[playing_file_index]][playing_body_id], copied_poses, insert_index, false);
            transls_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(transls_dic[npz_files[playing_file_index]][playing_body_id], copied_transls, insert_index, true);

            // Apply edit to laod cache
            //poses_dic[npz_files[playing_file_index]][playing_body_id] = poses[playing_body_id];
            //transls_dic[npz_files[playing_file_index]][playing_body_id] = transls[playing_body_id];
        }

    }
    public static NDArray Insert2DArray(NDArray original, NDArray insert_copy, int insertIndex, bool align_trans=false, bool align_orientation = false)
    {
        NDArray insert = insert_copy.copy();
        var originalShape = original.shape;
        var insertShape = insert.shape;


        if (originalShape.Length != 2 || insertShape.Length != 2)
        {
            throw new ArgumentException("Both original and insert NDArray must be 2-dimensional.");
        }

        if (originalShape[1] != insertShape[1])
        {
            throw new ArgumentException("Both original and insert NDArray must have the same number of columns.");
        }

        if(align_trans)
        {
            var insert_trans_difference = original[insertIndex] - insert[0];
            for (int i = 0; i < insert.shape[0]; i++)
            {
                insert[i] += insert_trans_difference;
            }
        }
        if (align_orientation)
        {
            var insert_orientation_difference = original[insertIndex][$":{3}"] - insert[0][$":{3}"];
            for (int i = 0; i < insert.shape[0]; i++)
            {
                insert[i][$":{3}"] += insert_orientation_difference;
            }
        }

        var resultShape = new Shape(originalShape[0] + insertShape[0], originalShape[1]);
        var result = np.zeros(resultShape);

        // Copy part before insert point into result
        result[$":{insertIndex+1}, :"] = original[$":{insertIndex+1}, :"];

        // Copy insert part into result 
        result[$"{insertIndex+1}:{insertIndex +1 + insertShape[0]}, :"] = insert;

        var last_part = original[$"{insertIndex+1}:, :"];
        if (align_trans)
        {
            int insert_length = insert.shape[0];
            var insert_difference = insert[insert_length - 1] - last_part[0];
            for (int i = 0; i < last_part.shape[0]; i++)
            {
                last_part[i] += insert_difference;
            }
        }
        if (align_orientation)
        {
            int insert_length = insert.shape[0];
            var last_orientation_difference = insert[insert_length - 1][$":{3}"] - last_part[0][$":{3}"];
            for (int i = 0; i < last_part.shape[0]; i++)
            {
                last_part[i][$":{3}"] += last_orientation_difference;
            }
        }
        result[$"{insertIndex +1 + insert.shape[0]}:, :"] = last_part;
                
        return result;
    }
    /*    public static NDArray DeleteRange(NDArray array, int start, int end, int axis = 0)
        {
            // Get the original shape
            var originalShape = array.shape;

            // check the index validation
            if (start < 0 || end >= originalShape[axis] || start > end)
            {
                throw new ArgumentException("Invalid start or end index.");
            }

            // Compute new shape
            var newShape = new int[originalShape.Length];
            Array.Copy(originalShape, newShape, originalShape.Length);
            newShape[axis] -= (end - start + 1);

            // create new NDArray
            var result = np.zeros(newShape);

            // construct the slice
            var beforeSlice = new Slice[originalShape.Length];
            var afterSlice = new Slice[originalShape.Length];
            var resultBeforeSlice = new Slice[originalShape.Length];
            var resultAfterSlice = new Slice[originalShape.Length];

            for (int i = 0; i < originalShape.Length; i++)
            {
                beforeSlice[i] = new Slice();
                afterSlice[i] = new Slice();
                resultBeforeSlice[i] = new Slice();
                resultAfterSlice[i] = new Slice();
            }

            beforeSlice[axis] = new Slice(stop: start);
            afterSlice[axis] = new Slice(start: end + 1);
            resultBeforeSlice[axis] = new Slice(stop: start);
            resultAfterSlice[axis] = new Slice(start: start);

            // copy part before deleted part to new NDArray
            result[resultBeforeSlice] = array[beforeSlice];

            // copy part after deleted part to new NDArray
            result[resultAfterSlice] = array[afterSlice];

            return result;
        }
    */
    public static NDArray DeleteRange(NDArray array, int start, int end, int axis = 0)
    {
        var originalShape = array.shape;

        // validate index
        if (start < 0 || end >= originalShape[axis] || start > end)
        {
            throw new ArgumentException("Invalid start or end index.");
        }

        // new shape
        var newShape = new int[originalShape.Length]; // keep the dimension number same
        Array.Copy(originalShape, newShape, originalShape.Length);
        newShape[axis] -= (end - start + 1);

        // ndarray to store result
        var result = np.zeros(newShape);

        // slicing
        if (axis == 0)
        {
            result[$":{start}, :"] = array[$":{start}, :"];
            result[$"{start}:{newShape[0]+1}, :"] = array[$"{end + 1}:, :"];
        }
        else if (axis == 1)
        {
            result[$":, :{start}"] = array[$":, :{start}"];
            result[$":, {start}:{newShape[1]}"] = array[$":, {end + 1}:"];
        }
        // reserve for higher dimensions.

        return result;
    }
    private static NDArray AdjustFrameRate(float target_fps, float old_fps, NDArray data)
    {
        if (data.ndim != 3)
        {
            throw new ArgumentException("can not adjust frame rate for ndim non-equal 2.");
        }

        int original_num_frames = data.shape[0];
        int new_num_frames = (int)Math.Round((original_num_frames * (double)target_fps) / old_fps);

        int columns = data.shape[1];
        NDArray adjusted_data = np.zeros((new_num_frames, columns));

        // Linear Interpolation
        for (int i = 0; i < new_num_frames; i++)
        {
            double t = (double)i / (new_num_frames - 1) * (original_num_frames - 1);
            int index = (int)t;
            double fraction = t - index;

            for (int j = 0; j < columns; j++)
            {
                if (index + 1 < original_num_frames)
                {
                    adjusted_data[i, j] = (1 - fraction) * data[index, j] + fraction * data[index + 1, j];
                }
                else
                {
                    adjusted_data[i, j] = data[index, j];
                }
            }
        }
        return adjusted_data;
    }
}   
