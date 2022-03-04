using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

internal class SwitchSignal : Bus
{
    volatile bool isStreaming;
    Thread job;
    POINT prevPosOnScr;

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
                if (prevPosOnScr.X + prevPosOnScr.Y == 0 && posOnScr.X + posOnScr.Y != 0)
                {
                    base.Send("Switch", JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        { "Key", "EMG" },
                        { "State", "Up" },
                    }));
                }
                if (prevPosOnScr.X + prevPosOnScr.Y != 0 && posOnScr.X + posOnScr.Y == 0)
                {
                    base.Send("Switch", JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        { "Key", "EMG" },
                        { "State", "Down" },
                    }));
                }
                prevPosOnScr = posOnScr;
                Thread.Sleep(100);
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
                            "NodeTypes", new List<string> { "Peripheral" }
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
                                        "Name", "Switch"
                                    },
                                    {
                                        "Details", null
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
