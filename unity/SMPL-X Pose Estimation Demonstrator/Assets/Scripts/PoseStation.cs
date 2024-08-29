using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Ionic.Zip;
using NumSharp;
using NumSharp.Utilities;
using System.Text.RegularExpressions;
using NumSharp.Generic;
using UnityEngine.XR.OpenXR.Input;
using System.Collections;

public class PoseStation : MonoBehaviour
{
    private string dataset_path_relative = "./Dataset";
    private string projectRootPath;
    private bool single_shape_paramenters = true;
    private FileSystemWatcher fileSystemWatcher;

    private string modify_file_prefix = "Modified@"; // prefix to mark the editted file.

    // dict that stores for each body ID the interested body instances
    private Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    private SMPLX m_SMPLX;

    private object lock_filenames = new object();
    private List<string> npz_files_stored = new List<string>(); // file names.
    private List<string> npz_files = new List<string>();    // buffer names.
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
    private object locker_dic = new object();
    private object locker_num_frames_dic = new object();
    /// @ Playing Infos
    private int playing_frame_index = -1;

    [Tooltip("Total played time.")]
    public float playing_timer = 0.0f;
    [Tooltip("Playing file index.")]
    public int playing_file_index = 0;
    private int playing_body_id = 0;

    /// @ Model Settings
    private int n_shape_components = 10;

    /// @ Player controll parameters
    private float boost_rate = 1;
    //[Tooltip("continue or stop, true for continue.")]
    private bool continue_stop = true; // true for continue, false for stop.
    private bool forward_backward = true; // true for forward, false for backward.
    private Slider progress_slider;
    private Dropdown boost_rate_dropdown;
    private Dropdown file_name_dropdown;
    private ControllUI m_ControllUI;
    private Canvas m_PlayerBoard;
    private Text total_time;
    private bool refresh_filenameDropdown = false;
    private object locker_file_name_dropdown = new object();
    private object locker_delete = new object();
    private object locker_create = new object();

    // For testing
    [Tooltip("copy test")]
    public bool test_copy = false;
    [Tooltip("paste test")]
    public bool test_paste = false;
    [Tooltip("cut test")]
    public bool test_cut = false;
    [Tooltip("replace test")]
    public bool test_replace = false;
    [Tooltip("Global Alignment test")]
    public bool test_ori = false;
    private bool test = false;
    [Tooltip("Translation Alignment test")]
    public bool test_transAlign = true;

    [Tooltip("Print npz_files")]
    public bool print_npz_files = false;

    // Start is called before the first frame update
    void Start()
    {        
        projectRootPath = Application.dataPath;  
        
        string dataset_path = Path.GetFullPath(Path.Combine(projectRootPath, dataset_path_relative));
                
        if (Directory.Exists(dataset_path))
        {
            string[] npz_files_paths = Directory.GetFiles(dataset_path, "*.npz", SearchOption.TopDirectoryOnly).Where(file => !file.EndsWith(".meta")).ToArray();

            npz_files_stored
               = npz_files_paths
                    .Select(file => Path.GetFileNameWithoutExtension(file))
                    .ToList();
            //npz_files = npz_files_stored;

            Debug.Log($"--------------npz files stored: \n{string.Join("\n", npz_files_paths)}");
            if (npz_files_paths.Length !=0)
            {
                for (int i = 0; i < npz_files_paths.Length; i++)
                {
                    if (File.Exists(npz_files_paths[i]))
                    {
                        //lock(lock_filenames)
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
                            /// Here reserved for multiple body datas in one .npz file,
                            /// the following Codes are not validated. 
                            for (int j=0; j<shapes_dic[npz_files[i]].shape[0];j++)
                            {
                                lock (locker_initialBodyPositionData)
                                    initialBodyPosition_dic[npz_files[i]] = compute_initial_trans(transls_dic[npz_files[i]], false);
                                lock(locker_num_frames_dic)
                                    num_frames_dic[npz_files[i]][j] = poses_dic[npz_files[i]][j].shape[0];
                            }
                        }
                    }
                }
                load_npz_success = true;

                if (shapes_dic[npz_files[playing_file_index]].ndim == 1)
                {
                    single_shape_paramenters = true;
                }
                else
                {
                    single_shape_paramenters = false;
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

        // 
        total_time = GameObject.Find("TotalTime").GetComponent<Text>();
        if (total_time == null)
        {
            Debug.Log($"in {this.name} TotalTime not Found!");
        }

        // Get Boost Rate Dropdown
        boost_rate_dropdown = GameObject.Find("BoostRateDropdown").GetComponent<Dropdown>();
        if (progress_slider == null)
        {
            Debug.Log($"in {this.name} BoostRateDropdown not Found!");
        }

        // Get File Name Dropdown
        file_name_dropdown = GameObject.Find("FileNameDropdown").GetComponent<Dropdown>();
        if (file_name_dropdown == null)
        {
            Debug.Log($"in {this.name} FileNameDropdown not Found!");
        }

        m_SMPLX = GameObject.Find("smplx_male_tcp").GetComponent<SMPLX>();
        if( m_SMPLX == null )
        {
            Debug.Log($"in {this.name} smplx_male_tcp not Found!");
        }

        m_ControllUI = GameObject.Find("PlayerControl").GetComponent<ControllUI>();
        if (m_ControllUI == null)
        {
            Debug.Log($"in {this.name} PlayerControl not Found!");
        }

        m_PlayerBoard = GameObject.Find("PlayerBoard").GetComponent<Canvas>();
        if (m_ControllUI == null)
        {
            Debug.Log($"in {this.name} PlayerBoard not Found!");
        }

        // Set system watcher.
        fileSystemWatcher = new FileSystemWatcher(dataset_path, "*.npz");
        fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;

        fileSystemWatcher.Created += OnCreate;
        fileSystemWatcher.Deleted += OnDelete;

        fileSystemWatcher.EnableRaisingEvents = true;

    }
    void OnDestroy()
    {
        poses_dic.Clear();
        shapes_dic.Clear();
        transls_dic.Clear();
        fps_dic.Clear();
        m_registeredBodies.Clear();
        fileSystemWatcher.EnableRaisingEvents = false;
        fileSystemWatcher.Dispose();
    }
    void Update()
    {
        // 0 walk 1 army
        if (print_npz_files)
        {
            Debug.Log($"npz_files:\n{string.Join("\n", npz_files)}");
            Debug.Log($"dic_keys:\n{string.Join("\n", poses_dic.Keys)}");
            print_npz_files = false;
        }
        if (test_copy)
        {
            playing_file_index = 0;
            copy_slice(0, 2, false);
            m_ControllUI.updateUI();
            test_copy = false;
        }
        if (test_paste)
        {
            playing_file_index = 1;
            copy_slice(0, 2, false);
            playing_file_index = 0;
            paste_slice(240, test_ori, test_transAlign);
            m_ControllUI.updateUI();
            test_paste = false;
        }
        if(test_cut)
        {
            playing_file_index = 0;
            cut_slice(2,5, test_ori);
            m_ControllUI.updateUI();
            test_cut = false;
        }
        if(test_replace)
        {
            playing_file_index = 1;
            copy_slice(0, 3, false);
            playing_file_index = 0;
            replace_slice(240, 600, test_ori);
            m_ControllUI.updateUI();
            test_replace = false;
        }
        if (test)
        {
            playing_file_index = 0;
            copy_slice(0, 2, false);
            playing_file_index = 1;
            paste_slice(948, true);
            test = false;
        }


        // Initialize Playing
        if (playing_timer == -1)
        {
            playing_timer = 0.0f;
            playing_frame_index = 0;
        }

        /// @ replaying control logic here.
        if(refresh_filenameDropdown)
        {
            //m_ControllUI.updateFilenameDropdown();
            //file_name_dropdown.RefreshShownValue();

            refresh_filenameDropdown = false;
            progress_slider.maxValue = num_frames_dic[npz_files[playing_file_index]][0] / fps_dic[npz_files[playing_file_index]];
            total_time.text = FormatTime(progress_slider.maxValue);
            file_name_dropdown.RefreshShownValue();
            file_name_dropdown.gameObject.SetActive(false);
            file_name_dropdown.gameObject.SetActive(true);
            Debug.Log($"refresh_filenameDropdown");
        }

        // No more file, reset to defaul pose and shape.
        if (npz_files.Count == 0)
        {
            continue_stop = false;
            m_SMPLX.ResetBodyPose();

            List<TcpControlledBody> subscribers;
            if (m_registeredBodies.TryGetValue(1, out subscribers))
            {
                foreach (TcpControlledBody sub in subscribers)
                {
                    sub.ResetBodyShape();
                }
            }
        }

        if (continue_stop)
        {
            if (forward_backward)
            {
                if (playing_frame_index < num_frames_dic[npz_files[playing_file_index]][playing_body_id]-3)
                {
                    playing_timer += Time.deltaTime * boost_rate;
                }
                // now forward played till end
                if (calculate_frame_index(playing_timer, fps_dic[npz_files[playing_file_index]]) >= num_frames_dic[npz_files[playing_file_index]][playing_body_id]-4)
                {
                    Debug.Log("CCC out of duration");
                    playing_frame_index = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - 3;
                    playing_timer = (num_frames_dic[npz_files[playing_file_index]][playing_body_id]-4) / fps_dic[npz_files[playing_file_index]];
                    boost_rate = 1;
                    boost_rate_dropdown.value = 2;
                    continue_stop = false;
                }
            }
            else
            {
                if (playing_frame_index > 0)
                {
                    playing_timer -= Time.deltaTime * boost_rate;
                }
                // now backward played till start
                if (playing_timer <= 0.01)
                {
                    playing_frame_index = 0;
                    playing_timer = 0;
                    //update_slider(playing_frame_index);
                    forward_backward = true;
                    boost_rate = 1;
                    boost_rate_dropdown.value = 2;
                    continue_stop = false;
                }
            }
        }
        //Debug.Log($"timer:{playing_timer},nfs: {poses_dic[npz_files[playing_file_index]].shape[0]}, idx: {playing_frame_index}/{num_frames_dic[npz_files[playing_file_index]][0]}");
        
        if (npz_files.Count > 0)
        {
            playing_frame_index = calculate_frame_index(playing_timer, fps_dic[npz_files[playing_file_index]]);

            if (single_shape_paramenters)
            {
                // Load one frame data for rendering
                float[] shape = new float[16];
                (float[] pose, float[] trans) = load_one_frame(poses_dic[npz_files[playing_file_index]], transls_dic[npz_files[playing_file_index]], playing_frame_index);

                // Swap yz axis to align coordinate system
                trans = _swap_translation_yz_axes_single(trans);

                shape = get_shape_single(shapes_dic[npz_files[playing_file_index]]);

                List<TcpControlledBody> subscribers;
                if (m_registeredBodies.TryGetValue(1, out subscribers))
                {
                    Vector3 initBodyPosition;
                    lock (locker_initialBodyPositionData)
                    {
                        initBodyPosition = initialBodyPosition_dic[npz_files[playing_file_index]][playing_body_id];
                    }
                    Vector3 translationDifferenceData = new Vector3(trans[0], trans[1], trans[2]) - initBodyPosition;
                    foreach (TcpControlledBody sub in subscribers)
                    {
                        //sub.SetParameters(translationDifferenceData, _add_y_angle_offset_to_pose(_add_x_angle_offset_to_pose(pose, -90), 180), _adapt_betas_shape(shape));
                        sub.SetParameters(translationDifferenceData, _add_x_angle_offset_to_pose(pose, -90), _adapt_betas_shape(shape));
                        //sub.SetParameters(translationDifferenceData, _add_x_angle_offset_to_pose(pose, -90), _adapt_betas_shape(shape));

                    }
                }
            }
            else
            {
                /// Here reserved for multiple body datas in one .npz file,
                /// the following Codes are not validated. 
                for (int i = 0; i < shapes_dic[npz_files[playing_file_index]].shape[0]; i++)
                {
                    // Load one frame data for rendering
                    float[] shape = new float[16];
                    (float[] pose, float[] trans) = load_one_frame(poses_dic[npz_files[playing_file_index]], transls_dic[npz_files[playing_file_index]], playing_frame_index);

                    trans = _swap_translation_yz_axes_single(trans);
                    shape = get_shape_single(shapes_dic[npz_files[playing_file_index]][i]);

                    List<TcpControlledBody> subscribers;
                    if (m_registeredBodies.TryGetValue(i + 1, out subscribers))
                    {
                        Vector3 initBodyPosition;
                        lock (locker_initialBodyPositionData)
                        {
                            initBodyPosition = initialBodyPosition_dic[npz_files[playing_file_index]][playing_body_id];
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
    }

    public bool get_loadNpzSucc() { return load_npz_success; }

    /// @@@ Watcher for dataset director changes
    private void OnDelete(object source, FileSystemEventArgs e)
    {
        lock (locker_delete)
        {
            Debug.Log($"{e.Name} is deleted.");
            string deleted_filename = Path.GetFileNameWithoutExtension(e.FullPath);
            //int deleted_index = npz_files.IndexOf(deleted_filename);
            string playing_filename = npz_files[playing_file_index];

            if (npz_files.IndexOf(deleted_filename) < 0)
            {
                throw new InvalidOperationException("Deleted file not in buffered files.");
            }
            else
            {
                poses_dic.Remove(deleted_filename);
                shapes_dic.Remove(deleted_filename);
                transls_dic.Remove(deleted_filename);
                fps_dic.Remove(deleted_filename);
                num_frames_dic.Remove(deleted_filename);
                initialBodyPosition_dic.Remove(deleted_filename);
                npz_files.Remove(deleted_filename);
                npz_files_stored.Remove(deleted_filename);

                int delete_option_value = GetDropDownOptionValueByText(file_name_dropdown, deleted_filename);

                if (file_name_dropdown.options.Count == 1)
                {
                    file_name_dropdown.ClearOptions();
                    playing_frame_index = -1;
                }
                else
                {
                    file_name_dropdown.options.RemoveAt(delete_option_value);
                }
                refresh_filenameDropdown = true;
                Debug.Log($"Deletion: refresh {refresh_filenameDropdown}");

                if (deleted_filename == playing_filename)
                {
                    playing_timer = 0.0f;
                    playing_file_index = 0;
                    file_name_dropdown.value = playing_file_index;
                }
                else if (npz_files.IndexOf(deleted_filename) < npz_files.IndexOf(playing_filename))
                {
                    playing_file_index = npz_files.IndexOf(playing_filename);
                    file_name_dropdown.value = playing_file_index;
                }
            }
        }
    }
    private static int GetDropDownOptionValueByText(Dropdown dd, string txt)
    {
        int res = -1;

        for (int i = 0; i<dd.options.Count; i++)
        {
            if (dd.options[i].text == txt) { res = i; break; }
        }    

        return res;
    }
    private void OnCreate(object source, FileSystemEventArgs e)
    {
        lock (locker_create)
        {

            // Add filename into buffer names and file names
            string new_file_name_path = e.FullPath;
            string npz_filename = Path.GetFileNameWithoutExtension(new_file_name_path);

            (poses_dic[npz_filename], shapes_dic[npz_filename], transls_dic[npz_filename], fps_dic[npz_filename])
                = _load_npz_attribute(new_file_name_path, "poses", "betas", "trans", "mocap_frame_rate");

            if(!npz_files.Contains(npz_filename))
                npz_files.Add(npz_filename);
            if(!npz_files_stored.Contains(npz_filename))
                npz_files_stored.Add(npz_filename);

            if (shapes_dic[npz_filename].ndim == 1)
            {
                lock (locker_initialBodyPositionData)
                    initialBodyPosition_dic[npz_filename] = compute_initial_trans(transls_dic[npz_filename], true);
                lock (locker_num_frames_dic)
                    num_frames_dic[npz_filename] = new int[] { poses_dic[npz_filename].shape[0] };
            }
            else
            {
                for (int i = 0; i < shapes_dic[npz_filename].shape[0]; i++)
                {
                    lock (locker_initialBodyPositionData)
                        initialBodyPosition_dic[npz_filename] = compute_initial_trans(transls_dic[npz_filename], false);
                    lock (locker_num_frames_dic)
                        num_frames_dic[npz_filename][i] = poses_dic[npz_filename][i].shape[0];
                }
            }

            if (file_name_dropdown.options.Count == 0)
            {
                file_name_dropdown.ClearOptions();
                file_name_dropdown.AddOptions(npz_files); 
                if (file_name_dropdown.options.Count > 0)
                {
                    file_name_dropdown.value = 0;
                }
                //file_name_dropdown.RefreshShownValue();
                //m_ControllUI.updateFilenameDropdown();

                playing_timer = -1;
            }
            else
            {
                if (GetDropDownOptionValueByText(file_name_dropdown, npz_filename) < 0)
                {
                    Dropdown.OptionData new_file = new Dropdown.OptionData(npz_filename);
                    file_name_dropdown.options.Add(new_file);
                    //file_name_dropdown.RefreshShownValue();
                }
            }
            refresh_filenameDropdown = true;
        }

        /*        file_name_dropdown.ClearOptions();
                Debug.Log($"npz_files: {string.Join(",", npz_files)}");
                file_name_dropdown.AddOptions(npz_files);*/
    }
 
    /// @@@ Load body pose data 

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
        string extract_path = Path.Combine(projectRootPath, "Dataset", npz_name);
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
        if (!File.Exists(poses_path) ||
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
        NDArray shapes_NDArray = null;
        NDArray poses_NDArray = null;
        NDArray trans_NDArray = null;
        float fps_value = -1;
        shapes_NDArray = np.load(betas_path).Clone();
        if (shapes_NDArray.ndim == 1)
        {
            single_shape_paramenters = true;
        }
        else
        {
            single_shape_paramenters = false;
        }
        poses_NDArray = np.load(poses_path).Clone();
        trans_NDArray = np.load(trans_path).Clone();
        if (File.Exists(fps_path))
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
        float[] pose_frame = get_frame(frame_index, poses);
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
    
    /// @@@ Functions for correcting original data for Unity. 
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
    /*private float[] _add_y_angle_offset_to_pose(float[] pose, float y_rot_angle_deg)
    {
        Vector3 rotation_vector = new Vector3(pose[0], pose[1], pose[2]);
        Quaternion quat = FromRotationVector(rotation_vector);
        Quaternion rotY = FromRotationVector(new Vector3(0, y_rot_angle_deg * Mathf.Deg2Rad, 0));
        Vector3 rotated = AsRotationVector(rotY * quat);

        pose[0] = rotated.x;
        pose[1] = rotated.y;
        pose[2] = rotated.z;

        return pose;
    }*/

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
            return new Vector3(q.x, q.y, q.z) * angle;
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

    /// @@@ Control of body model
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

    /// @@@ Replay functions (stop/continue, rewind, fast-forward)
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
        int res = 0;
        if ((int)Math.Floor(timer * frame_rate) >= 1)
        {
            res = (int)Math.Floor(timer * frame_rate)-1;
        }

        return (int)Math.Floor(timer * frame_rate);
        //return res;
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
        playing_frame_index = (int)(timer * fps_dic[npz_files[playing_file_index]])-1;
    }
    public void set_boost_rate(float new_boost_rate)
    {
        boost_rate = new_boost_rate;
    }
    public float get_booste_rate() { return boost_rate; }

    /// @ Function reserved for multiple body datas in one .npz file, not validated.
    public int get_num_body() { return single_shape_paramenters ? 1 : shapes_dic[npz_files[playing_file_index]].shape[0]; }
    public int get_playing_body_id() { return playing_body_id; }
    public void set_playing_body_id(int new_body_id) { playing_body_id = new_body_id; }
    
    /// @@@ Functions for File choose
    public List<string> get_npz_files()     {        return npz_files;    }
    public void change_file(int file_index)
    {
        if(file_index != playing_file_index)
        {
            // update playing_file_index
            playing_file_index = file_index;

            // Init playing state
            playing_timer = -1;
            continue_stop = false;
            forward_backward = true;
            playing_body_id = 0;

            // Init playing data
            if (shapes_dic[npz_files[playing_file_index]].ndim == 1)
            {
                single_shape_paramenters = true;
            }
            else
            {
                single_shape_paramenters = false;
            }
        }
    }

    /// @@@ Functions for outer controller use
    public int get_num_frames(int body_id )
    {
        //Debug.Log($"--------------------{npz_files[playing_file_index]} frames: {num_frames_dic[npz_files[playing_file_index]][body_id]}");
        return num_frames_dic[npz_files[playing_file_index]][body_id];
    }
    public float get_fps()
    {
        float fps = 0.0f;
        if(npz_files.Count > 0)
        {
            fps = fps_dic[npz_files[playing_file_index]];
        }

        return fps;
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

    /// @@@ Editor Functions

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
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]].copy(); }
            copied_poses = poses_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].copy();
            copied_transls = transls_dic[npz_files[playing_file_index]][$"{start_index}:{end_index + 1},:"].copy();
            copied_fps = fps_dic[npz_files[playing_file_index]];
            Debug.Log($"copy frames: {copied_num_frames}, buffer: {copied_poses.shape[0]}");
        }
        else
        {
            if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]][playing_body_id].copy(); }
            copied_poses = poses_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"].copy();
            copied_transls = transls_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"].copy();
            copied_fps = fps_dic[npz_files[playing_file_index]];
        }

    }
    public void cut_slice(float start_time, float end_time, bool align_orientation, bool align_transltion = true)
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

        // Determine wheather to build a new buffer to buffer the editted data.
        string cur_filename = npz_files[playing_file_index];
        string modified_filename = cur_filename;
        if (npz_files_stored.Contains(cur_filename))
        {
            if (IsPrefixed(cur_filename, modify_file_prefix)) // cur_filename has a prefix like "***Modified@" or "Modified@"
            {
                modified_filename = "@" + modified_filename;
                while (npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }
            else // cur_filename has no modified prefix.
            {
                modified_filename = modify_file_prefix + modified_filename;
                while (npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }
            
            poses_dic[modified_filename] = poses_dic[cur_filename].Clone();
            shapes_dic[modified_filename] = shapes_dic[cur_filename].Clone();
            transls_dic[modified_filename] = transls_dic[cur_filename].Clone();
            fps_dic[modified_filename] = fps_dic[cur_filename];
            int[] new_num_frames_dic = new int[num_frames_dic[cur_filename].Length];
            Array.Copy(num_frames_dic[cur_filename], new_num_frames_dic, num_frames_dic[cur_filename].Length);
            num_frames_dic[modified_filename] = new_num_frames_dic;
            Vector3[] new_initialBodyPosition_dic = new Vector3[initialBodyPosition_dic[cur_filename].Length];
            Array.Copy(initialBodyPosition_dic[cur_filename], new_initialBodyPosition_dic, initialBodyPosition_dic[cur_filename].Length);
            initialBodyPosition_dic[modified_filename] = new_initialBodyPosition_dic;

            if (!npz_files.Contains(modified_filename))
            {
                npz_files.Add(modified_filename);
                Dropdown.OptionData option_modified_filename = new Dropdown.OptionData(modified_filename);
                file_name_dropdown.options.Add(option_modified_filename);
            }

            playing_file_index = npz_files.IndexOf(modified_filename);
            file_name_dropdown.value = playing_file_index;
        }

        if (single_shape_paramenters)
        {
            copied_poses = poses_dic[npz_files[playing_file_index]][$"{start_index}:{end_index+1},:"].copy();
            copied_transls = transls_dic[npz_files[playing_file_index]][$"{start_index}:{end_index+1},:"].copy();
            copied_fps = fps_dic[npz_files[playing_file_index]];

            // Apply edit to playing cache
            num_frames_dic[npz_files[playing_file_index]][playing_body_id] -= copied_num_frames;
            Debug.Log($"copied frames: {copied_num_frames}, buffer: {copied_poses.shape[0]}");
            //poses_dic[npz_files[playing_file_index]] = DeleteRange(poses_dic[npz_files[playing_file_index]], start_index, end_index, align_orientation);
            //transls_dic[npz_files[playing_file_index]] = DeleteRange(transls_dic[npz_files[playing_file_index]], start_index, end_index, align_orientation);
            (transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]])
                = DeleteTransPose(transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]], start_index, end_index, align_orientation, align_transltion);
            Debug.Log($"cutted frames: {num_frames_dic[npz_files[playing_file_index]][playing_body_id]}, buffer: {poses_dic[npz_files[playing_file_index]].shape[0]}");
        }
        else
        {
            //if (copy_shape) { copied_shapes = shapes_dic[npz_files[playing_file_index]][playing_body_id]; }
            copied_poses = poses_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"];
            copied_transls = transls_dic[npz_files[playing_file_index]][playing_body_id][$"{start_index}:{end_index + 1},:"];
            copied_fps = fps_dic[npz_files[playing_file_index]];    
        }
    }
    public void replace_slice(int replace_start_index, int replace_end_index, bool align_orientation, bool align_transltion = true)
    {
        if (copied_fps != fps_dic[npz_files[playing_file_index]])
        {
            copied_poses = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_poses);
            copied_transls = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_transls);
        }

        // Determine wheather to build a new buffer to buffer the editted data.
        string cur_filename = npz_files[playing_file_index];
        if (npz_files_stored.Contains(cur_filename))
        {
            string modified_filename = cur_filename;
            if (IsPrefixed(cur_filename, modify_file_prefix)) // cur_filename has a prefix like "***Modified@" or "Modified@"
            {
                modified_filename = "@" + modified_filename;
                while (npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }
            else // cur_filename has no modified prefix.
            {
                modified_filename = modify_file_prefix + modified_filename;
                while (npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }

            poses_dic[modified_filename] = poses_dic[cur_filename].Clone();
            shapes_dic[modified_filename] = shapes_dic[cur_filename].Clone();
            transls_dic[modified_filename] = transls_dic[cur_filename].Clone();
            fps_dic[modified_filename] = fps_dic[cur_filename];
            int[] new_num_frames_dic = new int[num_frames_dic[cur_filename].Length];
            Array.Copy(num_frames_dic[cur_filename], new_num_frames_dic, num_frames_dic[cur_filename].Length);
            num_frames_dic[modified_filename] = new_num_frames_dic;
            Vector3[] new_initialBodyPosition_dic = new Vector3[initialBodyPosition_dic[cur_filename].Length];
            Array.Copy(initialBodyPosition_dic[cur_filename], new_initialBodyPosition_dic, initialBodyPosition_dic[cur_filename].Length);
            initialBodyPosition_dic[modified_filename] = new_initialBodyPosition_dic;

            if (!npz_files.Contains(modified_filename))
            {
                npz_files.Add(modified_filename);
                Dropdown.OptionData option_modified_filename = new Dropdown.OptionData(modified_filename);
                file_name_dropdown.options.Add(option_modified_filename);
            }

            playing_file_index = npz_files.IndexOf(modified_filename);
            file_name_dropdown.value = playing_file_index;
        }

        if (single_shape_paramenters)
        {
            num_frames_dic[npz_files[playing_file_index]][playing_body_id] = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - (replace_end_index - replace_start_index + 1) + copied_num_frames;
           /*(transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]])
                = InsertTransPose(DeleteRange(transls_dic[npz_files[playing_file_index]], replace_start_index, replace_end_index), copied_transls,
                                    DeleteRange(poses_dic[npz_files[playing_file_index]], replace_start_index, replace_end_index), copied_poses,
                                    replace_start_index - 1, align_orientation);*/
            
            (NDArray deleted_trans, NDArray deleted_pose) = (poses_dic[npz_files[playing_file_index]], transls_dic[npz_files[playing_file_index]])
                = DeleteTransPose(transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]], replace_start_index, replace_end_index, align_orientation);
            (transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]])
                = InsertTransPose(deleted_trans, copied_transls.Clone(), deleted_pose, copied_poses.Clone(), replace_start_index - 1, align_orientation);
        }
        else
        {
            num_frames_dic[npz_files[playing_file_index]][playing_body_id] = num_frames_dic[npz_files[playing_file_index]][playing_body_id] - (replace_end_index - replace_start_index + 1) + copied_num_frames;
            //poses_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(DeleteRange(poses_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_poses, replace_start_index - 1, false);
            //transls_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(DeleteRange(transls_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_transls, replace_start_index - 1, true);
            /*(transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]])
                = InsertTransPose(DeleteRange(transls_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_transls,
                                    DeleteRange(poses_dic[npz_files[playing_file_index]][playing_body_id], replace_start_index, replace_end_index), copied_poses,
                                    replace_start_index - 1, align_orientation);*/
        }
    }
    public void paste_slice(int insert_index, bool align_orientation, bool align_transltion = true)
    {
        if (copied_fps != fps_dic[npz_files[playing_file_index]])
        {
            copied_poses = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_poses);
            copied_transls = AdjustFrameRate(fps_dic[npz_files[playing_file_index]], copied_fps, copied_transls);
        }

        // Determine wheather to build a new buffer to buffer the editted data.
        string cur_filename = npz_files[playing_file_index];
        if (npz_files_stored.Contains(cur_filename))
        {
            string modified_filename = cur_filename;
            if (IsPrefixed(cur_filename, modify_file_prefix)) // cur_filename has a prefix like "***Modified@" or "Modified@"
            {
                modified_filename = "@" + modified_filename;
                while(npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }
            else // cur_filename has no modified prefix.
            {
                modified_filename = modify_file_prefix + modified_filename;
                while (npz_files_stored.Contains(modified_filename))
                {
                    modified_filename = "@" + modified_filename;
                }
            }

            poses_dic[modified_filename] = poses_dic[cur_filename].Clone();
            shapes_dic[modified_filename] = shapes_dic[cur_filename].Clone();
            transls_dic[modified_filename] = transls_dic[cur_filename].Clone();
            fps_dic[modified_filename] = fps_dic[cur_filename];
            int[] new_num_frames_dic = new int[num_frames_dic[cur_filename].Length];
            Array.Copy(num_frames_dic[cur_filename], new_num_frames_dic, num_frames_dic[cur_filename].Length);
            num_frames_dic[modified_filename] = new_num_frames_dic;
            Vector3[] new_initialBodyPosition_dic = new Vector3[initialBodyPosition_dic[cur_filename].Length];
            Array.Copy(initialBodyPosition_dic[cur_filename], new_initialBodyPosition_dic, initialBodyPosition_dic[cur_filename].Length);
            initialBodyPosition_dic[modified_filename] = new_initialBodyPosition_dic;

            if (!npz_files.Contains(modified_filename))
            {
                npz_files.Add(modified_filename);
                Dropdown.OptionData option_modified_filename = new Dropdown.OptionData(modified_filename);
                file_name_dropdown.options.Add(option_modified_filename);
            }

            playing_file_index = npz_files.IndexOf(modified_filename);
            file_name_dropdown.value = playing_file_index;
        }
        /*if (!cur_filename.StartsWith(modify_file_prefix))
        {
            string modified_filename = modify_file_prefix + cur_filename;
            while (npz_files_stored.Contains(modified_filename))
            {
                modified_filename = "*" + modified_filename;
            }
            npz_files.Add(modified_filename);

            poses_dic[modified_filename] = poses_dic[cur_filename];
            shapes_dic[modified_filename] = shapes_dic[cur_filename];
            transls_dic[modified_filename] = transls_dic[cur_filename];
            fps_dic[modified_filename] = fps_dic[cur_filename];
            num_frames_dic[modified_filename] = num_frames_dic[cur_filename];
            initialBodyPosition_dic[modified_filename] = initialBodyPosition_dic[cur_filename];

            playing_file_index = npz_files.IndexOf(modified_filename);
            Dropdown.OptionData option_modified_filename = new Dropdown.OptionData(modified_filename);
            file_name_dropdown.options.Add(option_modified_filename);

            file_name_dropdown.value = playing_file_index;
        }*/

        if (single_shape_paramenters)
        {
            Debug.Log($"ori frames: {num_frames_dic[npz_files[playing_file_index]][playing_body_id]}, buffer: {poses_dic[npz_files[playing_file_index]].shape[0]}");
            num_frames_dic[npz_files[playing_file_index]][playing_body_id] = num_frames_dic[npz_files[playing_file_index]][playing_body_id] + copied_num_frames;
            //poses_dic[npz_files[playing_file_index]] = Insert2DArray(poses_dic[npz_files[playing_file_index]], copied_poses, insert_index, false, align_orientation);
            //transls_dic[npz_files[playing_file_index]] = Insert2DArray(transls_dic[npz_files[playing_file_index]], copied_transls, insert_index, true, false);

            Debug.Log($"copied frames: {copied_num_frames}, buffer: {copied_poses.shape[0]}");
            (transls_dic[npz_files[playing_file_index]], poses_dic[npz_files[playing_file_index]]) 
                = InsertTransPose(transls_dic[npz_files[playing_file_index]], copied_transls.Clone(),
                poses_dic[npz_files[playing_file_index]], copied_poses.Clone(),
                insert_index, align_orientation, align_transltion);
            Debug.Log($"pasted frames: {num_frames_dic[npz_files[playing_file_index]][playing_body_id]}, buffer: {poses_dic[npz_files[playing_file_index]].shape[0]}");

        }
        else
        {
            //if (copy_shape)
            //{
            //    //shapes[playing_body_id] = copied_shapes;
            //    shapes_dic[npz_files[playing_file_index]][playing_body_id] = copied_shapes;
            //}
            //
            //num_frames_dic[npz_files[playing_file_index]][playing_body_id] += copied_num_frames;
            //poses_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(poses_dic[npz_files[playing_file_index]][playing_body_id], copied_poses, insert_index, false);
            //transls_dic[npz_files[playing_file_index]][playing_body_id] = Insert2DArray(transls_dic[npz_files[playing_file_index]][playing_body_id], copied_transls, insert_index, true);

            // Apply edit to laod cache
            //poses_dic[npz_files[playing_file_index]][playing_body_id] = poses[playing_body_id];
            //transls_dic[npz_files[playing_file_index]][playing_body_id] = transls[playing_body_id];
        }

    }
    private static bool IsPrefixed(string filename, string prefix)
    {
        // 构建正则表达式模式
        string pattern = @"^\@*" + Regex.Escape(prefix);

        // 使用正则表达式进行匹配
        return Regex.IsMatch(filename, pattern);
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
            Vector3 insert_0 
                = new Vector3(np.asscalar<float>(insert[0,0].Clone()),
                                np.asscalar<float>(insert[0,1].Clone()), 
                                np.asscalar<float>(insert[0,2].Clone()));
            Vector3 original_insertIndex 
                = new Vector3(np.asscalar<float>(original[insertIndex,0]), 
                                np.asscalar<float>(original[insertIndex,1]), 
                                np.asscalar<float>(original[insertIndex,2]));

            Quaternion q_insert_0 
                = Quaternion.AngleAxis(insert_0.magnitude * Mathf.Rad2Deg, insert_0.normalized);
            Quaternion q_original_insertIndex 
                = Quaternion.AngleAxis(original_insertIndex.magnitude * Mathf.Rad2Deg, original_insertIndex.normalized);
            Quaternion q_rel 
                = q_original_insertIndex * Quaternion.Inverse(q_insert_0);

            for (int i = 0; i < insert.shape[0]; i++)
            {
                Vector3 cur = new Vector3(np.asscalar<float>(insert[i, 0]), np.asscalar<float>(insert[i, 1]), np.asscalar<float>(insert[i, 2]));
                                
                Quaternion q_cur = Quaternion.AngleAxis(cur.magnitude*Mathf.Rad2Deg, cur.normalized);

                q_cur = q_rel * q_cur;

                q_cur.ToAngleAxis(out float theta, out cur);
                cur = cur * theta * Mathf.Deg2Rad;

                insert[i, 0] = cur.x;
                insert[i, 1] = cur.y;
                insert[i, 2] = cur.z;
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
            //var last_orientation_difference = insert[insert_length - 1][$":{3}"] - last_part[0][$":{3}"];
            var last_orientation_difference = last_part[0][$":{3}"] - insert[insert_length - 1][$":{3}"];

            Vector3 last_0 
                = new Vector3(np.asscalar<float>(last_part[0, 0]),
                                np.asscalar<float>(last_part[0, 1]),
                                np.asscalar<float>(last_part[0, 2]));
            Vector3 insert_end 
                = new Vector3(np.asscalar<float>(insert[insert_length - 1, 0]),
                                np.asscalar<float>(insert[insert_length - 1, 1]),
                                np.asscalar<float>(insert[insert_length - 1, 2]));

            Quaternion q_last_0 = Quaternion.AngleAxis(last_0.magnitude * Mathf.Rad2Deg, last_0.normalized);
            Quaternion q_insert_end = Quaternion.AngleAxis(insert_end.magnitude * Mathf.Rad2Deg, insert_end.normalized);
            Quaternion q_rel = q_insert_end * Quaternion.Inverse(q_last_0);

            for (int i = 0; i < last_part.shape[0]; i++)
            {
                Vector3 cur = new Vector3(np.asscalar<float>(last_part[i, 0]), np.asscalar<float>(last_part[i, 1]), np.asscalar<float>(last_part[i, 2]));

                Quaternion q_cur = Quaternion.AngleAxis(cur.magnitude * Mathf.Rad2Deg, cur.normalized);

                q_cur = q_rel * q_cur;

                q_cur.ToAngleAxis(out float theta, out cur);

                cur = cur * theta * Mathf.Deg2Rad;

                last_part[i, 0] = cur.x;
                last_part[i, 1] = cur.y;
                last_part[i, 2] = cur.z;
            }
        }
        
        result[$"{insertIndex +1 + insert.shape[0]}:, :"] = last_part;
                
        return result;
    }
    public static (NDArray, NDArray) InsertTransPose(NDArray trans_original,NDArray trans_copy,NDArray pose_original, NDArray pose_copy, int insertIndex, bool align_orientation = false, bool align_translation = true)
    {
        NDArray trans_insert = trans_copy.copy();
        NDArray pose_insert = pose_copy.copy();

        if (trans_original.shape.Length != 2 || pose_original.shape.Length != 2
            || trans_insert.shape.Length !=2 || pose_insert.shape.Length != 2)
        {
            throw new ArgumentException("Both original and insert NDArray must be 2-dimensional.");
        }

        if (trans_original.shape[1] != trans_insert.shape[1] || pose_original.shape[1] != pose_insert.shape[1])
        {
            throw new ArgumentException("Both original and insert NDArray must have the same number of columns.");
        }

        if (align_orientation)
        {
            Vector3 pose_insert_0 = new Vector3(np.asscalar<float>(pose_insert[0, 0]),
                                            np.asscalar<float>(pose_insert[0, 1]),
                                            np.asscalar<float>(pose_insert[0, 2]));
            Vector3 pose_original_insertIndex = new Vector3(np.asscalar<float>(pose_original[insertIndex, 0]),
                                                        np.asscalar<float>(pose_original[insertIndex, 1]),
                                                        np.asscalar<float>(pose_original[insertIndex, 2]));

            Quaternion q_pose_insert_0 = Quaternion.AngleAxis(pose_insert_0.magnitude * Mathf.Rad2Deg, pose_insert_0.normalized);
            Quaternion q_pose_original_insertIndex = Quaternion.AngleAxis(pose_original_insertIndex.magnitude * Mathf.Rad2Deg, pose_original_insertIndex.normalized);

            Quaternion q_pose_rel_all = q_pose_original_insertIndex * Quaternion.Inverse(q_pose_insert_0);
            Vector3 rel_EulerAngles = q_pose_rel_all.eulerAngles;

            Quaternion q_pose_rel = Quaternion.Euler(0,0, rel_EulerAngles.z);

            // Quaternion q_pose_rel = q_pose_original_insertIndex * Quaternion.Inverse(q_pose_insert_0);

            for (int i = 0; i < pose_insert.shape[0]; i++)
            {
                Vector3 pose_cur = new Vector3(np.asscalar<float>(pose_insert[i, 0]), np.asscalar<float>(pose_insert[i, 1]), np.asscalar<float>(pose_insert[i, 2]));
                Quaternion q_pose_cur = Quaternion.AngleAxis(pose_cur.magnitude * Mathf.Rad2Deg, pose_cur.normalized);
                               
                q_pose_cur = q_pose_rel * q_pose_cur;
                q_pose_cur.ToAngleAxis(out float theta, out pose_cur);
                pose_cur = pose_cur * theta * Mathf.Deg2Rad;

                pose_insert[i, 0] = pose_cur.x; pose_insert[i, 1] = pose_cur.y; pose_insert[i, 2] = pose_cur.z;
            }

            // Adjust corresponding translation orientation
            for (int i=0; i < trans_insert.shape[0]; i++)
            {
                Vector3 trans_insert_cur = new Vector3(np.asscalar<float>(trans_insert[i][0]), np.asscalar<float>(trans_insert[i][1]), np.asscalar<float>(trans_insert[i][2]));
                trans_insert_cur = q_pose_rel * trans_insert_cur;
                trans_insert[i, 0] = trans_insert_cur.x; trans_insert[i, 1] = trans_insert_cur.y; trans_insert[i, 2] = trans_insert_cur.z;
            }
        }

        // Align trans slice at beginning
        if (align_translation)
        {
            var trans_insert_difference_beggining = trans_original[insertIndex] - trans_insert[0];
            for (int i = 0; i < trans_insert.shape[0]; i++)
            {
                trans_insert[i] += trans_insert_difference_beggining;
            }
        }

        var trans_result_shape = new Shape(trans_original.shape[0] + trans_insert.shape[0], trans_original.shape[1]);
        var pose_result_shape = new Shape(pose_original.shape[0] + pose_insert.shape[0], pose_original.shape[1]);

        var trans_result = np.zeros(trans_result_shape);
        var pose_result = np.zeros(pose_result_shape);

        // Copy part before insert point into result
        trans_result[$":{insertIndex + 1}, :"] = trans_original[$":{insertIndex + 1}, :"];
        pose_result[$":{insertIndex + 1}, :"] = pose_original[$":{insertIndex + 1}, :"];

        // Copy insert part into result 
        trans_result[$"{insertIndex + 1}:{insertIndex + trans_insert.shape[0] + 1}, :"] = trans_insert;
        pose_result[$"{insertIndex + 1}:{insertIndex + pose_insert.shape[0] + 1}, :"] = pose_insert;

        // Slice remaining part of original out
        var trans_last_part = trans_original[$"{insertIndex + 1}:, :"];
        var pose_last_part = pose_original[$"{insertIndex + 1}:, :"]; 

        if (align_orientation)
        {
            int pose_insert_length = pose_insert.shape[0];

            Vector3 pose_last_0 = new Vector3(np.asscalar<float>(pose_last_part[0, 0]),
                                            np.asscalar<float>(pose_last_part[0, 1]),
                                            np.asscalar<float>(pose_last_part[0, 2]));
            Vector3 pose_insert_end = new Vector3(np.asscalar<float>(pose_insert[pose_insert_length - 1, 0]),
                                                np.asscalar<float>(pose_insert[pose_insert_length - 1, 1]),
                                                np.asscalar<float>(pose_insert[pose_insert_length - 1, 2]));

            Quaternion q_pose_last_0 = Quaternion.AngleAxis(pose_last_0.magnitude * Mathf.Rad2Deg, pose_last_0.normalized);
            Quaternion q_pose_insert_end = Quaternion.AngleAxis(pose_insert_end.magnitude * Mathf.Rad2Deg, pose_insert_end.normalized);

            Quaternion q_pose_rel_all = q_pose_insert_end * Quaternion.Inverse(q_pose_last_0);
            Vector3 rel_EulerAngles = q_pose_rel_all.eulerAngles;

            Quaternion q_pose_rel = Quaternion.Euler(0, 0, rel_EulerAngles.z);


            for (int i = 0; i < pose_last_part.shape[0]; i++)
            {
                Vector3 pose_cur = new Vector3(np.asscalar<float>(pose_last_part[i, 0]), np.asscalar<float>(pose_last_part[i, 1]), np.asscalar<float>(pose_last_part[i, 2]));
                Quaternion q_pose_cur = Quaternion.AngleAxis(pose_cur.magnitude * Mathf.Rad2Deg, pose_cur.normalized);

                q_pose_cur = q_pose_rel * q_pose_cur;
                q_pose_cur.ToAngleAxis(out float theta, out pose_cur);
                pose_cur = pose_cur * theta * Mathf.Deg2Rad;

                pose_last_part[i, 0] = pose_cur.x; pose_last_part[i, 1] = pose_cur.y; pose_last_part[i, 2] = pose_cur.z;
            }

            // Adjust corresponding translation orientation
            for (int i = 0; i < trans_last_part.shape[0]; i++)
            {
                Vector3 trans_last_part_cur = new Vector3(np.asscalar<float>(trans_last_part[i][0]), 
                                                            np.asscalar<float>(trans_last_part[i][1]), 
                                                            np.asscalar<float>(trans_last_part[i][2]));
                trans_last_part_cur = q_pose_rel * trans_last_part_cur;
                trans_last_part[i, 0] = trans_last_part_cur.x; 
                trans_last_part[i, 1] = trans_last_part_cur.y; 
                trans_last_part[i, 2] = trans_last_part_cur.z;
            }
        }

        if (align_translation)
        {        
            // Align trans slice at end.
            int trans_insert_length = trans_insert.shape[0];
            var trans_insert_difference_end = trans_insert[trans_insert_length - 1] - trans_last_part[0];
            for (int i = 0; i < trans_last_part.shape[0]; i++)
            {
                trans_last_part[i] += trans_insert_difference_end;
            }
        }

        // Add last part into result

            pose_result[$"{insertIndex + 1 + pose_insert.shape[0]}:, :"] = pose_last_part;
        

            trans_result[$"{insertIndex + 1 + trans_insert.shape[0]}:, :"] = trans_last_part;
        


        return (trans_result, pose_result);
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
    private static NDArray DeleteRange(NDArray array, int start, int end, bool align_orientation = false, int axis = 0)
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
    /*    private static (NDArray, NDArray)DeleteTransPose(NDArray trans_original, NDArray pose_original, int start, int end, bool align_orientation = false)
        {
            NDArray trans_delete = trans_original.copy();
            NDArray pose_delete = pose_original.copy();

            var trans_original_shape = trans_delete.shape;
            var pose_original_shape = pose_delete.shape;

            // validate index
            if (start < 0 || end >= trans_original_shape[0] || end >= pose_original_shape[0] || start > end)
            {
                throw new ArgumentException("Invalid start or end index.");
            }

            // new shape
            var trans_delete_shape = new int[trans_original_shape.Length];
            var pose_delete_shape = new int[pose_original_shape.Length];
            Array.Copy(trans_original_shape, trans_delete_shape, trans_delete_shape.Length);
            Array.Copy(pose_original_shape, pose_delete_shape, pose_original_shape.Length);
            trans_delete_shape[0] -= (end - start + 1);
            pose_delete_shape[0] -= (end - start + 1);

            // ndarray to store result
            NDArray trans_result = np.zeros(trans_delete_shape);
            NDArray pose_result = np.zeros(pose_delete_shape);

            // slicing

            // First part before delete range.
            trans_result[$":{start}, :"] = trans_delete[$":{start}, :"];
            pose_result[$":{start}, :"] = pose_delete[$":{start}, :"];

            // Align pose orientation after delete range.
            if (align_orientation)
            {
                Vector3 pose_delete_start = new Vector3(np.asscalar<float>(pose_delete[start - 1, 0]),
                                                    np.asscalar<float>(pose_delete[start - 1, 1]),
                                                    np.asscalar<float>(pose_delete[start - 1, 2]));
                Vector3 pose_delete_end = new Vector3(np.asscalar<float>(pose_delete[end + 1, 0]),
                                                    np.asscalar<float>(pose_delete[end + 1, 1]),
                                                    np.asscalar<float>(pose_delete[end + 1, 2]));

                Quaternion q_pose_delete_start = Quaternion.AngleAxis(pose_delete_start.magnitude * Mathf.Rad2Deg, pose_delete_start.normalized);
                Quaternion q_pose_delete_end = Quaternion.AngleAxis(pose_delete_end.magnitude * Mathf.Rad2Deg, pose_delete_end.normalized);

                Quaternion q_pose_rel_all = q_pose_delete_start * Quaternion.Inverse(q_pose_delete_end);
                Vector3 rel_EulerAngles = q_pose_rel_all.eulerAngles;

                Quaternion q_pose_rel = Quaternion.Euler(0, 0, rel_EulerAngles.z);

                for (int i = end + 1; i < pose_delete.shape[0]; i++)
                    {
                        Vector3 pose_cur = new Vector3(np.asscalar<float>(pose_delete[i, 0]),
                                                        np.asscalar<float>(pose_delete[i, 1]),
                                                        np.asscalar<float>(pose_delete[i, 2]));
                        Quaternion q_pose_cur = Quaternion.AngleAxis(pose_cur.magnitude * Mathf.Rad2Deg, pose_cur.normalized);

                        q_pose_cur = q_pose_rel * q_pose_cur;
                        q_pose_cur.ToAngleAxis(out float theta, out pose_cur);
                        pose_cur = pose_cur * theta * Mathf.Deg2Rad;

                        pose_delete[i, 0] = pose_cur.x; pose_delete[i, 1]=pose_cur.y; pose_delete[i, 2] = pose_cur.z;
                    }

                // Align corresponding translation orientation
                for (int i = end + 1; i < trans_delete.shape[0]; i++)
                {
                    Vector3 trans_cur = new Vector3(np.asscalar<float>(trans_delete[i, 0]),
                                                    np.asscalar<float>(trans_delete[i, 1]),
                                                    np.asscalar<float>(trans_delete[i, 2]));
                    trans_cur = q_pose_rel * trans_cur;

                    trans_delete[i, 0] = trans_cur.x;
                    trans_delete[i, 1] = trans_cur.y;
                    trans_delete[i, 2] = trans_cur.z;
                }
            }

            // Align trans after delete range.
            var trans_delete_difference = trans_delete[start] - trans_delete[end];
            for (int i = end + 1; i < trans_delete.shape[0]; i++)
            {
                trans_delete[i] += trans_delete_difference;
            }

            trans_result[$"{start}:{trans_delete_shape[0]}, :"] = trans_delete[$"{end + 1}:, :"];
            pose_result[$"{start}:{pose_delete_shape[0]}, :"] = pose_delete[$"{end + 1}:, :"];

            return (trans_result, pose_result);
        }*/
    public static (NDArray, NDArray) InsertDeletePart(NDArray trans_original, NDArray trans_copy, NDArray pose_original, NDArray pose_copy, int insertIndex, bool align_orientation = false, bool align_translation = true)
    {
        NDArray trans_insert = trans_copy.copy();
        NDArray pose_insert = pose_copy.copy();

        if (trans_original.shape.Length != 2 || pose_original.shape.Length != 2
            || trans_insert.shape.Length != 2 || pose_insert.shape.Length != 2)
        {
            throw new ArgumentException("Both original and insert NDArray must be 2-dimensional.");
        }

        if (trans_original.shape[1] != trans_insert.shape[1] || pose_original.shape[1] != pose_insert.shape[1])
        {
            throw new ArgumentException("Both original and insert NDArray must have the same number of columns.");
        }

        if (align_orientation)
        {
            Vector3 pose_insert_0 = new Vector3(np.asscalar<float>(pose_insert[0, 0]),
                                            np.asscalar<float>(pose_insert[0, 1]),
                                            np.asscalar<float>(pose_insert[0, 2]));
            Vector3 pose_original_insertIndex = new Vector3(np.asscalar<float>(pose_original[insertIndex, 0]),
                                                        np.asscalar<float>(pose_original[insertIndex, 1]),
                                                        np.asscalar<float>(pose_original[insertIndex, 2]));

            Quaternion q_pose_insert_0 = Quaternion.AngleAxis(pose_insert_0.magnitude * Mathf.Rad2Deg, pose_insert_0.normalized);
            Quaternion q_pose_original_insertIndex = Quaternion.AngleAxis(pose_original_insertIndex.magnitude * Mathf.Rad2Deg, pose_original_insertIndex.normalized);

            Quaternion q_pose_rel_all = q_pose_original_insertIndex * Quaternion.Inverse(q_pose_insert_0);
            Vector3 rel_EulerAngles = q_pose_rel_all.eulerAngles;

            Quaternion q_pose_rel = Quaternion.Euler(0, 0, rel_EulerAngles.z);

            // Quaternion q_pose_rel = q_pose_original_insertIndex * Quaternion.Inverse(q_pose_insert_0);

            for (int i = 0; i < pose_insert.shape[0]; i++)
            {
                Vector3 pose_cur = new Vector3(np.asscalar<float>(pose_insert[i, 0]), np.asscalar<float>(pose_insert[i, 1]), np.asscalar<float>(pose_insert[i, 2]));
                Quaternion q_pose_cur = Quaternion.AngleAxis(pose_cur.magnitude * Mathf.Rad2Deg, pose_cur.normalized);

                q_pose_cur = q_pose_rel * q_pose_cur;
                q_pose_cur.ToAngleAxis(out float theta, out pose_cur);
                pose_cur = pose_cur * theta * Mathf.Deg2Rad;

                pose_insert[i, 0] = pose_cur.x; pose_insert[i, 1] = pose_cur.y; pose_insert[i, 2] = pose_cur.z;
            }

            // Adjust corresponding translation orientation
            for (int i = 0; i < trans_insert.shape[0]; i++)
            {
                Vector3 trans_insert_cur = new Vector3(np.asscalar<float>(trans_insert[i][0]), np.asscalar<float>(trans_insert[i][1]), np.asscalar<float>(trans_insert[i][2]));
                trans_insert_cur = q_pose_rel * trans_insert_cur;
                trans_insert[i, 0] = trans_insert_cur.x; trans_insert[i, 1] = trans_insert_cur.y; trans_insert[i, 2] = trans_insert_cur.z;
            }
        }

        // Align trans slice at beginning
        if (align_translation)
        {
            var trans_insert_difference_beggining = trans_original[insertIndex] - trans_insert[0];
            for (int i = 0; i < trans_insert.shape[0]; i++)
            {
                trans_insert[i] += trans_insert_difference_beggining;
            }
        }

        var trans_result_shape = new Shape(trans_original.shape[0] + trans_insert.shape[0], trans_original.shape[1]);
        var pose_result_shape = new Shape(pose_original.shape[0] + pose_insert.shape[0], pose_original.shape[1]);

        var trans_result = np.zeros(trans_result_shape);
        var pose_result = np.zeros(pose_result_shape);

        // Copy part before insert point into result
        trans_result[$":{trans_original.shape[0]}, :"] = trans_original[$":{trans_original.shape[0]}, :"];
        pose_result[$":{pose_original.shape[0]}, :"] = pose_original[$":{pose_original.shape[0]}, :"];

        // Copy insert part into result 
        trans_result[$"{trans_original.shape[0]}:, :"] = trans_insert;
        pose_result[$"{pose_original.shape[0]}:, :"] = pose_insert;

        return (trans_result, pose_result);
    }
    private static (NDArray, NDArray) DeleteTransPose(NDArray trans_original, NDArray pose_original, int start, int end, bool align_orientation = false, bool align_transltion = true)
    {
        NDArray trans_first = trans_original[$":{start}, :"].copy();
        NDArray pose_first = pose_original[$":{start}, :"].copy();
        NDArray trans_last = trans_original[$"{end+1}:, :"].copy();
        NDArray pose_last = pose_original[$"{end+1}:, :"].copy();

        (NDArray trans_result, NDArray pose_result) = InsertDeletePart(trans_first, trans_last, pose_first, pose_last, start - 1, align_orientation, align_transltion);


        return (trans_result, pose_result);
    }
    private static NDArray AdjustFrameRate(float target_fps, float old_fps, NDArray data)
    {
        if (data.ndim != 2)
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
    public void save_npz()
    {
        string dataset_path = Path.GetFullPath(Path.Combine(Application.dataPath, dataset_path_relative));
        SavePoseToNpz(dataset_path, npz_files[playing_file_index],
            poses_dic[npz_files[playing_file_index]], shapes_dic[npz_files[playing_file_index]],
            transls_dic[npz_files[playing_file_index]], fps_dic[npz_files[playing_file_index]]);

    }
    private static void SavePoseToNpz(string filePath, string fileName, NDArray poses, NDArray betas, NDArray trans, float mocap_frame_rate)
    {
        string npyPath = filePath + "/" + fileName;
        Directory.CreateDirectory(npyPath);
        np.save(npyPath  + "/poses.npy", poses);
        np.save(npyPath + "/betas.npy", betas);
        np.save(npyPath + "/trans.npy", trans);
        SaveSingleFloatToNpy(npyPath + "/mocap_frame_rate.npy", mocap_frame_rate);

        // .npz file path
        string npzFilePath = filePath + "/" + fileName + ".npz";

        // zip .npy files into .npz file
        using (ZipFile zip = new ZipFile())
        {
            zip.AddFile(npyPath + "/mocap_frame_rate.npy", "").FileName = "mocap_frame_rate.npy";
            zip.AddFile(npyPath + "/trans.npy", "").FileName = "trans.npy";
            zip.AddFile(npyPath + "/poses.npy", "").FileName = "poses.npy";
            zip.AddFile(npyPath + "/betas.npy", "").FileName = "betas.npy";
            zip.Save(npzFilePath);
        }
    }
    // @ Function for saving single value to a non-array-structured .npy file.
    private static void SaveSingleFloatToNpy(string filePath, float singleFloat)
    {
        double singleVaule = (double)singleFloat;
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            using (var writer = new BinaryWriter(fs))
            {
                // write .npy header
                writer.Write((byte)0x93); // magic string
                writer.Write(Encoding.ASCII.GetBytes("NUMPY"));
                writer.Write((byte)0x01); // major version number
                writer.Write((byte)0x00); // minor version number

                // Descriptor
                string header = "{'descr': '<f8', 'fortran_order': False, 'shape': (), }";
                int paddingLength = 64 - ((10 + header.Length) % 64);
                header = header.PadRight(header.Length + paddingLength);

                writer.Write((short)header.Length); // header length
                writer.Write(Encoding.ASCII.GetBytes(header)); // header content

                // write data
                byte[] floatBytes = BitConverter.GetBytes(singleVaule);
                if (BitConverter.IsLittleEndian == false)
                {
                    Array.Reverse(floatBytes);
                }
                writer.Write(floatBytes);
            }
        }
    }
    private static string FormatTime(float totalSeconds)
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
}   
