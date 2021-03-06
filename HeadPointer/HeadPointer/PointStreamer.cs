using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

internal class PointStreamer : Bus
{
    volatile bool isStreaming;
    Thread job;

    string SerialNumber = "SerialNumber";
    string Model = "HeadPointer";
    string Generation = "HP1";
    string FirmwareVersion = "1.0";
    float SamplingFrequency = 33;

    public void start()
    {
        this.Init(6502);
        this.BusEvent += OnBusEvent;
        this.MessageEvent += OnMessageEvent;

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
                    { "x", posOnScr.X / SystemParameters.PrimaryScreenWidth },
                    { "y", posOnScr.Y / SystemParameters.PrimaryScreenHeight },
                    { "pv", 1 },
                    { "ts", DateTime.Now.Ticks / 10 },
                }));
                base.Send("EyeTracker", JsonConvert.SerializeObject(new Dictionary<string, object>
                {
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

    private void OnBusEvent(object sender, BusEventArgs e)
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
                    }));
                break;
            default:
                break;
        }
    }

    private void OnMessageEvent(object sender, MessageEventArgs e)
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
}
