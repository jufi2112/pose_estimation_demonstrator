using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;

[Obsolete("Use TcpControlledBody instead")]
public class TcpPoseClient : MonoBehaviour
{
    // from https://gist.github.com/danielbierwirth/0636650b005834204cb19ef5ae6ccedb

    #region private members
    private TcpClient m_socketConnection;
    private Thread m_clientReceiveThread;
    private SMPLX m_smplxScript = null;
    private System.Object poseLocker = new System.Object();
    private float[] m_bodyPose = new float[165];
    private float[] m_lastBodyPose = new float[165];
    private float[] m_bodyNewTranslation = new float[3];
    private float[] m_bodyLastTranslation = new float[3];
    private Vector3 m_initialPositionBody;
    private bool m_receivedInitialPositionData = false;
    private Vector3 m_initialPositionData;
    private bool m_bodyTransformSetUp = false;
    #endregion

    public string m_tcpIP = "localhost";
    public int m_tcpPort = 7777;
    public int m_interestedBodyPoseID = 1;

    // Start is called before the first frame update
    void Start()
    {
        m_smplxScript = gameObject.GetComponent<SMPLX>();
        ConnectToTcpServer();
    }

    // Update is called once per frame
    void Update()
    {
        float[] bodyPoseBuffer;
        float[] bodyTranslationBuffer;
        lock (poseLocker)
        {
            bodyTranslationBuffer = (float[]) m_bodyNewTranslation.Clone();
            bodyPoseBuffer = (float[]) m_bodyPose.Clone();
        }
        if (!ArraysEqual<float>(bodyPoseBuffer, m_lastBodyPose))
        {
            if (!m_bodyTransformSetUp)
            {
                gameObject.transform.Rotate(-90.0f, 0.0f, 0.0f);
                gameObject.transform.position = new Vector3(gameObject.transform.position.x, 0.95f, gameObject.transform.position.z);
                m_initialPositionBody = gameObject.transform.position;
                m_bodyTransformSetUp = true;
            }
            m_smplxScript.SetBodyPose(bodyPoseBuffer);
            m_lastBodyPose = bodyPoseBuffer;
        }
        if (!ArraysEqual<float>(bodyTranslationBuffer, m_bodyLastTranslation))
        {
            if (!m_receivedInitialPositionData)
            {
                m_initialPositionData = new Vector3(bodyTranslationBuffer[0], bodyTranslationBuffer[2], bodyTranslationBuffer[1]);
                m_receivedInitialPositionData = true;
            }
            if (m_bodyTransformSetUp)
            {
                Vector3 newTranslationFromData = new Vector3(bodyTranslationBuffer[0], bodyTranslationBuffer[2], bodyTranslationBuffer[1]);
                gameObject.transform.position = m_initialPositionBody + (newTranslationFromData - m_initialPositionData);
                m_bodyLastTranslation = bodyTranslationBuffer;
            }
        }
        // if (Input.GetKeyDown(KeyCode.Space))
        // {
        //     SendMessageToServer("This is a message from one of your clients.");
        // }
    }

    void OnDestroy()
    {
        CloseSocketConnection();
    }

    private void ConnectToTcpServer()
    {
        try
        {
            m_clientReceiveThread = new Thread(new ThreadStart(ListenForData));
            m_clientReceiveThread.IsBackground = true;
            m_clientReceiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.Log("On Client connect exception " + e);
        }
    }

    private void ListenForData()
    {
        try
        {
            m_socketConnection = new TcpClient(m_tcpIP, m_tcpPort);
            Byte[] bytes = new Byte[676];
            while (m_socketConnection != null)
            {
                // Get stream object for reading
                using (NetworkStream stream = m_socketConnection.GetStream())
                {
                    int bytesRead;
                    // read incoming stream into byte array
                    while ((bytesRead = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        if (bytesRead != 676)
                        {
                            Debug.Log("Unable to read 676 bytes, skipping message");
                            continue;
                        }
                        if (m_socketConnection == null)
                        {
                            break;
                        }
                        //Byte[] bytesBodyID = new ArraySegment<Byte>(bytes, 0, 4);
                        //Debug.Log("BodyID Bytes Length: " + bytesBodyID.Length);
                        //Debug.Log(bytesBodyID);
                        int body_id = DeserializeBodyID(bytes);
                        if (body_id != m_interestedBodyPoseID)
                        {
                            continue;
                        }
                        // 168 values each 4 bytes --> 672 bytes
                        (float[] transl, float[] pose) = DeserializeNumpyStream<float>(bytes);
                        if (m_smplxScript == null)
                        {
                            Debug.Log("Could not apply pose as no valid SMPLX script could be found!");
                            continue;
                        }
                        lock (poseLocker)
                        {
                            m_bodyNewTranslation = transl;
                            m_bodyPose = pose;
                        }
                    }
                    Debug.Log("Server closed the connection");
                    CloseSocketConnection(stream);
                }
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket exception: " + socketException);
        }
    }

    private void SendMessageToServer(string clientMessage)
    {
        if (m_socketConnection == null)
        {
            return;
        }
        try
        {
            // Get stream object for writing
            NetworkStream stream = m_socketConnection.GetStream();
            if (stream.CanWrite)
            {
                // convert string message to byte array
                byte[] clientMessageAsByteArray = Encoding.ASCII.GetBytes(clientMessage);
                // Write byte array to socketConnection stream
                stream.Write(clientMessageAsByteArray, 0, clientMessageAsByteArray.Length);
                Debug.Log("Client send his message - should be received by server");
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket exception: " + socketException);
        }
    }

    private void CloseSocketConnection(NetworkStream stream = null)
    {
        if (m_socketConnection == null)
        {
            return;
        }
        try
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
            m_socketConnection.Close();
            m_socketConnection = null;
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket exception: " + socketException);
        }
    }

    private (T[], T[]) DeserializeNumpyStream<T>(byte[] bytes)
    {
        T[] transl = new T[12 / Marshal.SizeOf(typeof(T))];
        T[] pose = new T[660 / Marshal.SizeOf(typeof(T))];
        Buffer.BlockCopy(bytes, 4, transl, 0, 12);
        Buffer.BlockCopy(bytes, 16, pose, 0, 660);
        return (transl, pose);
    }

    private int DeserializeBodyID(byte[] bytes)
    {
        Byte[] bodyIDBytes = new Byte[4];
        for (int i = 0; i < 4; ++i)
        {
            bodyIDBytes[i] = bytes[i];
        }
        // Data are streamed as Big Endian
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bodyIDBytes);
        }
        int result = BitConverter.ToInt32(bodyIDBytes, 0);
        //Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private void LogArray<T>(T[] array)
    {
        for (int i = 0; i < array.Length; ++i)
        {
            Debug.Log("Index " + i + " | Element: " + array[i]);
        }
    }

    private bool ArraysEqual<T>(T[] arr1, T[] arr2)
    {
        if (arr1.Length != arr2.Length)
        {
            return false;
        }
        for (int i = 0; i < arr1.Length; ++i)
        {
            if (!EqualityComparer<T>.Default.Equals(arr1[i], arr2[i]))
            {
                return false;
            }
        }
        return true;
    }
}
