using System.Collections;
using System.Collections.Generic;
using System.IO;
//using System.IO.Compression;
using System;
using System.Linq;
using System.Text;

using UnityEngine;
using NumSharp;
using NumSharp.Utilities;
using Ionic.Zip;
using System.Text.RegularExpressions;
public static class NpySaver
{
    public static void SaveFloatToNpy(float value, string filePath)
    {
        // Prepare .npy header and data
        var header = CreateNpyHeader(value);
        var data = BitConverter.GetBytes(value);
        Debug.Log($"byte data: {string.Join("",data)}");
        // Write magic string, version, header length, header, and data to .npy file
        using (var writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
        {
            WriteMagicString(writer);
            WriteVersion(writer);
            WriteHeaderLength(writer, header);
            WriteHeader(writer, header);
            WriteData(writer, data);
        }
    }

    private static void WriteMagicString(BinaryWriter writer)
    {
        // Write magic string "\x93NUMPY"
        char[] magic = {'N', 'U', 'M', 'P', 'Y'};
        writer.Write((byte)147);
        writer.Write(magic);
    }

    private static void WriteVersion(BinaryWriter writer)
    {
        // Write version (major=1, minor=0)
        writer.Write((byte)1);  // major version
        writer.Write((byte)0);  // minor version
    }

    private static void WriteHeaderLength(BinaryWriter writer, string header)
    {
        // Write header length (ushort)
        writer.Write((ushort)header.Length);
    }

    private static void WriteHeader(BinaryWriter writer, string header)
    {
        // Write header as ASCII bytes
        //writer.Write(Encoding.ASCII.GetBytes(header));
        for (int i = 0; i < header.Length; i++)
            writer.Write((byte)header[i]);
    }

    private static void WriteData(BinaryWriter writer, byte[] data)
    {
        // Write data bytes
        writer.Write(data);
    }

    private static string CreateNpyHeader(float value)
    {
        // Generate .npy header for a single float value

        var header = "{'descr': '<f8', 'fortran_order': False, 'shape': (), }";

        int headerSize = header.Length + 10;
        int pad = 16 - (headerSize % 16);
        
        return header.PadRight(header.Length+pad, ' ') + "\n";
    }
}


public class test : MonoBehaviour
{
    string path = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\army_poses_stageii\mocap_frame_rate.npy";
    string path_npz = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\army_poses_stageii.npz";
    string path_betas = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\jumping_jacks_stageii\betas.npy";
    string path_fps = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\jumping_jacks_stageii\mocap_frame_rate.npy";
    string path_poses = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Dataset\jumping_jacks_stageii\poses.npy";
    string test_path = @"J:\0-EDU\0-SoSe-2024\0-Lernveranstaltungen\MA-PR\KP_CG_Vis\Code\pose_estimation_demonstrator\unity\SMPL-X Pose Estimation Demonstrator\Assets\Test";
    string save = @"D:\Dataset\";
    // Start is called before the first frame update
    void Start()
    {
        /*        NDArray shapes = np.load(path_betas);
                NDArray poses = np.load(path_poses);
                Debug.Log($"poses.Shape{poses.shape[0]}");
                float fps_single = 120;
                NDArray fps_arr = np.asscalar<float>(fps_single);
                np.save(save + "pose_0", poses[0]);
                np.save(save + "shapes", shapes);
                //np.save(save + "mocap_fps", fps_arr);
                NpySaver.SaveFloatToNpy(fps_single, save + "mocap_fps.npy");
                var zip = new ZipFile();
                zip.AddFile(save + "pose_0.npy", "");
                zip.AddFile(save + "shapes.npy", "");
                zip.AddFile(save + "mocap_frame_rate.npy", "");
                zip.Save(save + "new.npz");*/

        float fps = 120;

        // 创建一个单个浮点数
        double singleFloat = 59.99;

        string filePath = save + "mocap_fps.npy";
        // 创建文件流
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            using (var writer = new BinaryWriter(fs))
            {
                // 写入 .npy 文件头部
                writer.Write((byte)0x93); // magic string
                writer.Write(Encoding.ASCII.GetBytes("NUMPY"));
                writer.Write((byte)0x01); // major version number
                writer.Write((byte)0x00); // minor version number

                // 构建描述符
                string header = "{'descr': '<f8', 'fortran_order': False, 'shape': (), }";
                int paddingLength = 64 - ((10 + header.Length) % 64);
                header = header.PadRight(header.Length + paddingLength);

                writer.Write((short)header.Length); // header length
                writer.Write(Encoding.ASCII.GetBytes(header)); // header content
                //writer.Write("\n");

                // 写入浮点数数据
                // 写入浮点数数据（小端字节序）
                byte[] floatBytes = BitConverter.GetBytes(singleFloat);
                if (BitConverter.IsLittleEndian == false)
                {
                    Array.Reverse(floatBytes);
                }
                writer.Write(floatBytes);
            }
        }


        ///----------------------------------------------------------------
        /// Pack .npz methos

        
        /*string npzFilePath = save + "testNpz.npz";
        using (FileStream zipToOpen = new FileStream(npzFilePath, FileMode.Create))
        {
            using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(path_fps, "mocap_frame_rate.npy");
                archive.CreateEntryFromFile(path_poses, "poses.npy");
                archive.CreateEntryFromFile(filePath, "mocap_fps.npy");
            }
        }*/
        /*

        // 定义 .npz 文件的保存路径
        string npzFilePath = save + "testNpz.npz";

        // 创建 .npz 文件并将 .npy 文件添加到其中
        using (ZipFile zip = new ZipFile())
        {
            zip.AddFile(path_fps, "").FileName = "mocap_frame_rate.npy";
            zip.AddFile(path_poses, "").FileName = "poses.npy";
            zip.AddFile(filePath, "").FileName = "mocap_fps.npy";
            zip.Save(npzFilePath);
        }

        ///----------------------------------------------------------------
*/




    }

    static bool IsValidPrefix(string filename, string prefix)
    {
        // 构建正则表达式模式
        string pattern = @"^\**" + Regex.Escape(prefix);

        // 使用正则表达式进行匹配
        return Regex.IsMatch(filename, pattern);
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


}

