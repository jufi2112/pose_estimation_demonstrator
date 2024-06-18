using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

using UnityEngine;
using NumSharp;
using NumSharp.Utilities;

public class test : MonoBehaviour
{
    string path = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\army_poses_stageii\mocap_frame_rate.npy";
    string path2 = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\army_poses_stageii.npz";
    string path3 = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\jumping_jacks_stageii\betas.npy";
    string path4 = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\jumping_jacks_stageii\poses.npy";
    // Start is called before the first frame update
    void Start()
    {
        //var data = np.Load<float[]>(path);
        var stream = new FileStream(path, FileMode.Open);
        /*var reader = new BinaryReader(stream, System.Text.Encoding.ASCII);*/

        using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII
            ))
        {
            int bytes;
            Type type;
            int[] shape;
            
            if (!parseReader(reader, out bytes, out type, out shape))
                throw new FormatException();
            Shape shp = new Shape(1);
            Debug.Log("shp: " + shp);
            Debug.Log("shape l: " + shape.Length); 

            var buffer = new byte[bytes * 1];
            Array array;
            if(shape.Length == 0)
            {
                array = Array.CreateInstance(type, 1);
            }
            else
            {
                 array = Arrays.Create(type, shape.Aggregate((dims, dim) => dims * dim));
            }

            var result = new NDArray(readValueMatrix(reader, array, bytes, type, shape));
            Debug.Log("result: " + result);
            Debug.Log("result shp: " + result.Shape[0]);

            var res = get_fps(result);
            Debug.Log("fps: " + res);


            /*var data = get_frame(0, result);
            Debug.Log("data: " + string.Join(",", data));*/

        }
        float get_fps(NDArray datas)
        {
            float fps;

            fps = np.asscalar<float>(datas);

            return fps;
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
        Debug.Log("type:" + type);
        Debug.Log("bytes:" + bytes);
        Debug.Log("isLittleEndian:" + isLittleEndian);

        if (isLittleEndian.HasValue && isLittleEndian.Value == false)
            throw new Exception();

        mark = "'fortran_order': ";
        s = header.IndexOf(mark) + mark.Length;
        e = header.IndexOf(",", s + 1);
        bool fortran = bool.Parse(header.Substring(s, e - s));
        Debug.Log("fortran:" + fortran +" - " + (fortran==true));

        if (fortran)
            throw new Exception();

        mark = "'shape': (";
        s = header.IndexOf(mark) + mark.Length;
        e = header.IndexOf(")", s+1);
        if(e>0)
        {
            shape = header.Substring(s, e - s).Split(',').Where(v => !String.IsNullOrEmpty(v)).Select(Int32.Parse).ToArray();
        }
        else
        {
            shape = new int[0];
        }
        Debug.Log("shape s:" + s);
        Debug.Log("shape e:" + e);

        // shape = new int[] {1};

        Debug.Log("shape:" + shape);

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


    // Update is called once per frame
    void Update()
    {
        
    }
}
