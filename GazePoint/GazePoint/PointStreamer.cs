using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

internal class PointStreamer : Bus
{
    bool isStreaming;
    Thread job;

    string SerialNumber = "SerialNumber";
    string Model = "GazePoint";
    string Generation = "GP3";
    string FirmwareVersion = "1.0";
    float SamplingFrequency = 33;

    public void start()
    {
        this.Init(6502);
        this.NodeEvent += OnNodeEvent;
        this.BusEvent += OnBusEvent;

        Task.Run(() => 
        {
            gp();
        });

        if (job?.IsAlive == true)
        {
            return;
        }

        isStreaming = true;
        job = new Thread(() =>
        {
            while (isStreaming)
            {
                GetCursorPos(out POINT posOnScr);
                base.Send("EyeTracker", JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    { "NodeId", base.NodeId },
                    { "x", fpogx },
                    { "y", fpogy },
                    { "pv", fpog_valid },
                    { "ts", DateTime.Now.Ticks / 10 },
                }));
                base.Send("EyeTracker", JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    { "NodeId", base.NodeId },
                    { "lx" , 0 },
                    { "ly" , 0 },
                    { "lz" , 0 },
                    { "lv" , 1 },
                    { "rx" , 0 },
                    { "ry" , 0 },
                    { "rz" , 0 },
                    { "rv" , 1 },
                    { "ts" , DateTime.Now.Ticks / 10 },
                }));
                Thread.Sleep(30);
            }
        });
        job.Start();
    }

    private void OnNodeEvent(object sender, NodeEventArgs e)
    {
        switch (e.Type)
        {
            case "NodeAdded":
                base.Send(e.NodeId, JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        {
                            "NodeTypes", new List<string> { "Pointer" }
                        },
                        {
                            "NodeId", base.NodeId
                        },
                        {
                            "NodePath", Assembly.GetExecutingAssembly().Location
                        },
                        {
                            "SendChannels", new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    {
                                        "Name", "EyeTracker"
                                    },
                                    {
                                        "Details", new Dictionary<string, object>
                                        {
                                            { "SerialNumber", SerialNumber },
                                            { "Model", Model },
                                            { "Generation", Generation },
                                            { "FirmwareVersion", FirmwareVersion },
                                            { "SamplingFrequency", SamplingFrequency },
                                        }
                                    },
                                },
                            }
                        },
                        {
                            "ReceiveChannels", null
                        },
                        {
                            "Id", Guid.NewGuid().ToString()
                        },
                        {
                            "TimeStamp", $"{DateTime.UtcNow:o}"
                        },
                    }));
                break;
            default:
                break;
        }
    }

    private void OnBusEvent(object sender, BusEventArgs e)
    {
    }

    public void stop()
    {
        isStreaming = false;
        if (!job.Join(500))
        {
            job?.Abort();
        }

        base.Quit();
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    double time_val;
    double fpogx;
    double fpogy;
    int fpog_valid;
    private void gp()
    {
        const int ServerPort = 4242;
        const string ServerAddr = "127.0.0.1";

        bool exit_state = false;
        int startindex, endindex;
        TcpClient gp3_client;
        NetworkStream data_feed;
        StreamWriter data_write;
        String incoming_data = "";

        ConsoleKeyInfo keybinput;

        // Try to create client object, return if no server found
        try
        {
            gp3_client = new TcpClient(ServerAddr, ServerPort);
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to connect with error: {0}", e);
            return;
        }

        // Load the read and write streams
        data_feed = gp3_client.GetStream();
        data_write = new StreamWriter(data_feed);

        // Setup the data records
        data_write.Write("<SET ID=\"ENABLE_SEND_TIME\" STATE=\"1\" />\r\n");
        data_write.Write("<SET ID=\"ENABLE_SEND_POG_FIX\" STATE=\"1\" />\r\n");
        data_write.Write("<SET ID=\"ENABLE_SEND_CURSOR\" STATE=\"1\" />\r\n");
        data_write.Write("<SET ID=\"ENABLE_SEND_DATA\" STATE=\"1\" />\r\n");

        // Flush the buffer out the socket
        data_write.Flush();

        do
        {
            int ch = data_feed.ReadByte();
            if (ch != -1)
            {
                incoming_data += (char)ch;

                // find string terminator ("\r\n") 
                if (incoming_data.IndexOf("\r\n") != -1)
                {
                    // only process DATA RECORDS, ie <REC .... />
                    if (incoming_data.IndexOf("<REC") != -1)
                    {
                        // Process incoming_data string to extract FPOGX, FPOGY, etc...
                        startindex = incoming_data.IndexOf("TIME=\"") + "TIME=\"".Length;
                        endindex = incoming_data.IndexOf("\"", startindex);
                        time_val = Double.Parse(incoming_data.Substring(startindex, endindex - startindex));

                        startindex = incoming_data.IndexOf("FPOGX=\"") + "FPOGX=\"".Length;
                        endindex = incoming_data.IndexOf("\"", startindex);
                        fpogx = Double.Parse(incoming_data.Substring(startindex, endindex - startindex));

                        startindex = incoming_data.IndexOf("FPOGY=\"") + "FPOGY=\"".Length;
                        endindex = incoming_data.IndexOf("\"", startindex);
                        fpogy = Double.Parse(incoming_data.Substring(startindex, endindex - startindex));

                        startindex = incoming_data.IndexOf("FPOGV=\"") + "FPOGV=\"".Length;
                        endindex = incoming_data.IndexOf("\"", startindex);
                        fpog_valid = Int32.Parse(incoming_data.Substring(startindex, endindex - startindex));
                    }

                    incoming_data = "";
                }
            }
        }
        while (exit_state == false);

        data_write.Close();
        data_feed.Close();
        gp3_client.Close();
    }
}
