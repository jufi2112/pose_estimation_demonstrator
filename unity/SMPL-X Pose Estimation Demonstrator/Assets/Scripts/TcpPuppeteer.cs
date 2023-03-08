using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;

public class TcpPuppeteer : MonoBehaviour
{
    #region private members
    private TcpClient m_socketConnection = null;
    private Thread m_clientReceiveThread = null;
    private System.Object locker = new System.Object();
    // dict that stores for each body ID the interested body instances
    Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    // dict that stores for each body ID the initial position of the data
    volatile Dictionary<int, Vector3> m_initialBodyPositionsData = new Dictionary<int, Vector3>();
    // dict to store updated translation and pose information for each body ID, updated by TCP thread and read by main thread
    Dictionary<int, Dictionary<string, float[]>> m_nextBodyInformation = new Dictionary<int, Dictionary<string, float[]>>();
    Dictionary<int, Dictionary<string, float[]>> m_appliedBodyInformation = new Dictionary<int, Dictionary<string, float[]>>();
    volatile bool m_stopThread = false;
    #endregion

    #region public members
    public string m_tcpIP = "localhost";
    public int m_tcpPort = 7777;
    public bool m_connectAtStart = true;
    public int m_messageLengthBytes = 676;
    [Tooltip("This allows to convert between the coordinate systems used for capturing and used in Unity. For each unity axis, provide the corresponding axis from the captured data, e.g. [0, 2, 1] will mean that the last axis of the capture coordinate system will be used as height in Unity.")]
    public Vector3Int m_translationElementOrder = new Vector3Int(0,2,1);
    #endregion
    // Start is called before the first frame update
    void Start()
    {
        if (m_connectAtStart)
            ConnectToTcpServer();
    }

    void OnDestroy()
    {
        m_stopThread = true;
        m_clientReceiveThread.Join();
    }

    // Update is called once per frame
    void Update()
    {
        // !!! IMPORTANT !!!
        // Convention: elements in arrays (e.g. float[]) will always be in the order as received from the server (e.g. height axis may not correspond to Unity's Y-axis)
        // Elements in Vectors (e.g. Vector3) will always have been converted to Unity's coordinate system using m_translationElementOrder
        Dictionary<int, Dictionary<string, float[]>> buffer;
        lock (locker)
        {
            buffer = new Dictionary<int, Dictionary<string, float[]>>(m_nextBodyInformation);
        }
        foreach (KeyValuePair<int, Dictionary<string, float[]>> entry in buffer)
        {
            if (m_appliedBodyInformation.ContainsKey(entry.Key))
            {
                if ((ArraysEqual<float>(m_appliedBodyInformation[entry.Key]["transl"], entry.Value["transl"])) &&
                    (ArraysEqual<float>(m_appliedBodyInformation[entry.Key]["pose"], entry.Value["pose"])))
                    continue;
            }
            // Notify registered bodies of new translation and / or pose
            List<TcpControlledBody> subscribers;
            if (m_registeredBodies.TryGetValue(entry.Key, out subscribers))
            {
                float[] translBuffer = entry.Value["transl"];
                // Vector from initial position to current position with respect to the streamed data
                Vector3 translationDifferenceData = new Vector3(
                    translBuffer[m_translationElementOrder[0]],
                    translBuffer[m_translationElementOrder[1]],
                    translBuffer[m_translationElementOrder[2]]
                ) - m_initialBodyPositionsData[entry.Key];
                foreach (TcpControlledBody sub in subscribers)
                {
                    sub.SetParameters(translationDifferenceData, entry.Value["pose"]);
                }
            }
            m_appliedBodyInformation[entry.Key] = new Dictionary<string, float[]>
            {
                ["transl"] = entry.Value["transl"],
                ["pose"] = entry.Value["pose"]
            };
        }
    }

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
            Debug.Log("On client connect exception: " + e);
        }
    }

    private void ListenForData()
    {
        try
        {
            m_socketConnection = new TcpClient(m_tcpIP, m_tcpPort);
            Byte[] bytes = new Byte[m_messageLengthBytes];
            while (!m_stopThread)
            {
                if (m_socketConnection == null)
                    break;
                // Get stream object for reading
                using (NetworkStream stream = m_socketConnection.GetStream())
                {
                    int bytesRead;
                    // read incoming stream into byte array
                    while (((bytesRead = stream.Read(bytes, 0, bytes.Length)) != 0) && (!m_stopThread))
                    {
                        if (bytesRead != m_messageLengthBytes)
                        {
                            Debug.Log("Unable to read " + m_messageLengthBytes + " bytes (received " + bytesRead + " bytes instead), skipping message");
                            continue;
                        }
                        (int bodyID, float[] transl, float[] pose) = DeserializeNumpyStream<float>(bytes);
                        if (!m_initialBodyPositionsData.ContainsKey(bodyID))
                        {
                            m_initialBodyPositionsData[bodyID] = new Vector3(
                                transl[m_translationElementOrder[0]],
                                transl[m_translationElementOrder[1]],
                                transl[m_translationElementOrder[2]]
                            );
                            Debug.Log("Body ID " + bodyID + " initial translation: " + m_initialBodyPositionsData[bodyID]);
                        }
                        lock (locker)
                        {
                            m_nextBodyInformation[bodyID] = new Dictionary<string, float[]>
                            {
                                ["transl"] = transl,
                                ["pose"] = pose
                            };
                        }
                    }
                    if (!m_stopThread)
                        Debug.Log("Server closed the connection");
                    else
                        Debug.Log("Closing thread");
                    CloseSocketConnection(stream);
                }
            }          
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket exception: " + socketException);
        }
        catch (ThreadAbortException)
        {
            CloseSocketConnection(m_socketConnection.GetStream());
            Debug.Log("Closing thread due to Abort Request");
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
    private (int, T[], T[]) DeserializeNumpyStream<T>(byte[] bytes)
    {
        int bodyID = DeserializeBodyID(bytes); 
        T[] transl = new T[12 / Marshal.SizeOf(typeof(T))];
        T[] pose = new T[660 / Marshal.SizeOf(typeof(T))];
        Buffer.BlockCopy(bytes, 4, transl, 0, 12);
        Buffer.BlockCopy(bytes, 16, pose, 0, 660);
        return (bodyID, transl, pose);
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
