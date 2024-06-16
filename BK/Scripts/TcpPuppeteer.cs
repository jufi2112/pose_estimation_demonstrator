using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class TcpPuppeteer : MonoBehaviour
{
    #region private members
    private TcpClient m_socketConnection = null;
    private Thread m_clientReceiveThread = null;
    // lock to be used when accessing m_nextBodyInformation
    private readonly System.Object locker_nextBodyInformation = new System.Object();
    // lock to be used when accessing m_initialBodyPositionsData
    private readonly System.Object locker_initialBodyPositionData = new System.Object();
    // lock to be used when accessing m_transmissionFrequency
    private readonly System.Object locker_transmissionTimers = new System.Object();
    // dict that stores for each body ID the interested body instances
    Dictionary<int, List<TcpControlledBody>> m_registeredBodies = new Dictionary<int, List<TcpControlledBody>>();
    // dict that stores for each body ID the initial position of the data
    Dictionary<int, Vector3> m_initialBodyPositionsData = new Dictionary<int, Vector3>();
    // dict to store updated translation and pose information for each body ID, updated by TCP thread and read by main thread
    Dictionary<int, Dictionary<string, float[]>> m_nextBodyInformation = new Dictionary<int, Dictionary<string, float[]>>();
    Dictionary<int, Dictionary<string, float[]>> m_appliedBodyInformation = new Dictionary<int, Dictionary<string, float[]>>();
    // Dict that stores stopwatch instances that measure the transmission period for each body ID
    Dictionary<int, Stopwatch> m_transmissionStopwatches = new Dictionary<int, Stopwatch>();
    // Dict that stores the transmission frequency for each body ID
    Dictionary<int, int> m_transmissionFrequency = new Dictionary<int, int>();
    volatile bool m_stopThread = false;
    #endregion

    #region public members
    [Tooltip("IP address of the TCP server to which the client should connect.")]
    public string m_tcpIP = "localhost";
    [Tooltip("Port of the TCP server to which the client should connect.")]
    public int m_tcpPort = 7777;
    public bool m_connectAtStart = true;
    [Tooltip("Body ID (4 bytes) + translation (3 * 4 bytes) + shape (10 * 4 bytes) + pose (165 * 4 bytes) = 716 bytes.")]
    public int m_messageLengthBytes = 716;
    [Tooltip("Time in seconds to wait for the TCP listener script to gracefully shut down. After this time has passed, Abort is called.")]
    public int m_threadCloseGracePeriodSeconds = 3;
    [Tooltip("Whether information about the transmission frequency of the body parameters should be shown.")]
    public bool m_showTransmissionFrequencies = true;
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
        bool threadClosed = m_clientReceiveThread.Join(m_threadCloseGracePeriodSeconds * 1000);
        if (!threadClosed)
        {
            Debug.Log("TCP listener thread had to be aborted");
            m_clientReceiveThread.Abort();
        }
    }

    // Update is called once per frame
    void Update()
    {
        Dictionary<int, Dictionary<string, float[]>> buffer;
        lock (locker_nextBodyInformation)
        {
            // Only one frame
            buffer = new Dictionary<int, Dictionary<string, float[]>>(m_nextBodyInformation);
        }
        foreach (KeyValuePair<int, Dictionary<string, float[]>> entry in buffer)
        {
            // Checkt wheather the trans and pose information are applied.
            if (m_appliedBodyInformation.ContainsKey(entry.Key))
            {
                if ((ArraysEqual<float>(m_appliedBodyInformation[entry.Key]["transl"], entry.Value["transl"])) &&
                    (ArraysEqual<float>(m_appliedBodyInformation[entry.Key]["shape"], entry.Value["shape"])) &&
                    (ArraysEqual<float>(m_appliedBodyInformation[entry.Key]["pose"], entry.Value["pose"])))
                    continue;
            }
            // Notify registered bodies of new translation and / or pose
            List<TcpControlledBody> subscribers;
            if (m_registeredBodies.TryGetValue(entry.Key, out subscribers))
            {
                // Vector from initial position to current position with respect to the streamed data
                Vector3 initBodyPosition;
                lock (locker_initialBodyPositionData)
                {
                    initBodyPosition = m_initialBodyPositionsData[entry.Key];
                }
                Vector3 translationDifferenceData = new Vector3(entry.Value["transl"][0], entry.Value["transl"][1], entry.Value["transl"][2]) - initBodyPosition;
                foreach (TcpControlledBody sub in subscribers)
                {
                    sub.SetParameters(translationDifferenceData, entry.Value["pose"], entry.Value["shape"]);
                    // sub.SetParameters(translationDifferenceData, entry.Value["pose"]);
                }
            }
            // Update the applied body information.
            m_appliedBodyInformation[entry.Key] = new Dictionary<string, float[]>
            {
                ["transl"] = entry.Value["transl"],
                ["shape"] = entry.Value["shape"],
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

    void OnGUI()
    {
        if (m_showTransmissionFrequencies)
        {
            Dictionary<int, int> transmFreq;
            lock(locker_transmissionTimers)
            {
                transmFreq = new Dictionary<int, int>(m_transmissionFrequency);
            }
            int i = 0;
            GUIStyle defaultStyle = GUI.skin.label;
            foreach (var item in transmFreq)
            {
                string text = "Body ID " + item.Key + ": " + item.Value + " Hz";
                Vector2 size = defaultStyle.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(10, (i+1) * 10 + size.y, size.x, size.y), text);
                i++;
            }
        }
    }

    private void ConnectToTcpServer()
    {
        try
        {
            // Create a Thread for getting the Data (from AMASS?).
            m_clientReceiveThread = new Thread(new ThreadStart(ListenForData));
            m_clientReceiveThread.IsBackground = true;
            m_clientReceiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.Log("On client connect exception: " + e);
        }
    }

    // Listener for Input.
    private void ListenForData()
    {
        try
        {
            m_socketConnection = new TcpClient(m_tcpIP, m_tcpPort); // Connection to Tcp Client.
            Byte[] bytes = new Byte[m_messageLengthBytes]; // Data length: Body ID (4 bytes) + translation (3 * 4 bytes) + pose (165 * 4 bytes) = 676 bytes
            Debug.Log($"bytes.Length: {bytes.Length}");
            while (!m_stopThread)
            {
                if (m_socketConnection == null) // No connection to tcp client, then do nothing.
                    break;
                // Get stream object for reading
                using (NetworkStream stream = m_socketConnection.GetStream())
                {
                    int bytesRead;
                    // read incoming stream into byte array
                    while (((bytesRead = stream.Read(bytes, 0, bytes.Length)) != 0) && (!m_stopThread)) // Read one frame data, .Read() return the byte size that is readed.
                    {
                        if (bytesRead != m_messageLengthBytes)
                        {
                            // Illeagal Msg will be skipped.
                            Debug.Log("Unable to read " + m_messageLengthBytes + " bytes (received " + bytesRead + " bytes instead), skipping message");
                            continue;
                        }
                        (int bodyID, float[] transl, float[] shape, float[] pose) = DeserializeNumpyStream<float>(bytes);
                        // (int bodyID, float[] transl, float[] pose) = DeserializeNumpyStream<float>(bytes);
                        if (m_transmissionStopwatches.ContainsKey(bodyID))
                        {
                            double time_passed = m_transmissionStopwatches[bodyID].Elapsed.TotalSeconds;
                            m_transmissionStopwatches[bodyID].Restart();
                            if (time_passed != 0)
                            {
                                lock(locker_transmissionTimers)
                                {
                                    m_transmissionFrequency[bodyID] = (int)Mathf.Round(1 / (float)time_passed);
                                }
                            }
                            else
                            {
                                Debug.Log("Got 0 for time passed since last update for body index " + bodyID);
                            }
                        }
                        else
                        {
                            m_transmissionStopwatches[bodyID] = Stopwatch.StartNew();
                        }
                        lock (locker_initialBodyPositionData)
                        {
                            if (!m_initialBodyPositionsData.ContainsKey(bodyID))
                            {
                                m_initialBodyPositionsData[bodyID] = new Vector3(transl[0], transl[1], transl[2]);
                            }
                        }
                        lock (locker_nextBodyInformation)
                        {
                            m_nextBodyInformation[bodyID] = new Dictionary<string, float[]>
                            {
                                ["transl"] = transl,
                                ["shape"] = shape,
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
    private (int, T[], T[], T[]) DeserializeNumpyStream<T>(byte[] bytes)
    // private (int, T[], T[]) DeserializeNumpyStream<T>(byte[] bytes)
    {
        int bodyID = DeserializeBodyID(bytes); 
        T[] transl = new T[12 / Marshal.SizeOf(typeof(T))];
        T[] shape = new T[40 / Marshal.SizeOf(typeof(T))];
        T[] pose = new T[660 / Marshal.SizeOf(typeof(T))];
        Buffer.BlockCopy(bytes, 4, transl, 0, 12);
        Buffer.BlockCopy(bytes, 16, shape, 0, 40);
        Buffer.BlockCopy(bytes, 56, pose, 0, 660);
        return (bodyID, transl, shape, pose);
        // Buffer.BlockCopy(bytes, 16, pose, 0, 660);
        // return (bodyID, transl, pose);
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
