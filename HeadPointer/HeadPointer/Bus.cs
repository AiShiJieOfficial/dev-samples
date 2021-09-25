using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Bus
{
    public event EventHandler<NodeEventArgs> NodeEvent;
    public event EventHandler<BusEventArgs> BusEvent;
    public string NodeId;
    public Dictionary<string, RawNode> Nodes;

    NetMQBeacon m_beacon;
    NetMQPoller m_poller;
    PublisherSocket m_publisher;
    SubscriberSocket m_subscriber;

    BlockingCollection<NetMQMessage> sendQueue = new BlockingCollection<NetMQMessage>();
    Action sendJob;

    public Bus()
    {
        NodeId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        Nodes = new Dictionary<string, RawNode>();
    }

    int m_randomPort;
    public void Init(int udpPort)
    {
        Task.Run(() =>
        {
            NetMQTimer timer = new NetMQTimer(TimeSpan.FromSeconds(1));
            timer.Elapsed += Timer_Elapsed;

            using (m_subscriber = new SubscriberSocket())
            using (m_publisher = new PublisherSocket())
            using (m_beacon = new NetMQBeacon())
            using (m_poller = new NetMQPoller { m_subscriber, m_beacon, timer })
            {
                m_subscriber.Subscribe("Public");
                m_subscriber.Subscribe(NodeId);
                m_subscriber.ReceiveReady += M_subscriber_ReceiveReady;

                m_randomPort = m_publisher.BindRandomPort("tcp://*");

                m_beacon.Configure(udpPort);
                m_beacon.Publish($"{NodeId}:{m_randomPort}", TimeSpan.FromSeconds(1));
                m_beacon.Subscribe("");
                m_beacon.ReceiveReady += M_beacon_ReceiveReady;

                m_poller.Run();
            }
        });
    }

    public void Subscribe(string channel)
    {
        m_subscriber?.Subscribe(channel);
    }

    public void Send(string channel, string msg)
    {
        if (sendJob == null)
        {
            sendJob = () =>
            {
                while (true)
                {
                    m_publisher?.SendMultipartMessage(sendQueue.Take());
                }
            };
            Task.Run(sendJob);
        }
        var m = new NetMQMessage(2);
        m.Append(channel);
        m.Append(msg);
        sendQueue.Add(m);
    }

    public void Quit()
    {
        m_poller.Stop();
    }

    private void M_subscriber_ReceiveReady(object sender, NetMQSocketEventArgs e)
    {
        var msg = m_subscriber.ReceiveMultipartMessage();
        BusEvent?.Invoke(this, new BusEventArgs { Channel = msg[0].ConvertToString(), Message = msg[1].ConvertToString() });
    }

    private async void M_beacon_ReceiveReady(object sender, NetMQBeaconEventArgs e)
    {
        var msg = m_beacon.Receive();
        var beacon = msg.String.Split(':');
        var nodeId = beacon[0];
        int.TryParse(beacon[1], out int port);

        var endPoint = $"tcp://{msg.PeerHost}:{port}";
        if (!Nodes.ContainsKey(nodeId))
        {
            Nodes[nodeId] = new RawNode { EndPoint = endPoint, LastAlive = DateTime.Now };
            m_subscriber.Connect(endPoint);
            await Task.Delay(1500);
            NodeEvent?.Invoke(this, new NodeEventArgs { Type = "NodeAdded", NodeId = nodeId });
        }
        else Nodes[beacon[0]].LastAlive = DateTime.Now;
    }

    private void Timer_Elapsed(object sender, NetMQTimerEventArgs e)
    {
        Nodes
            .Where(n => DateTime.Now > n.Value.LastAlive + TimeSpan.FromSeconds(5))
            .ToList()
            .ForEach(kv =>
            {
                Nodes.Remove(kv.Key);
                m_subscriber.Disconnect(kv.Value.EndPoint);
                NodeEvent?.Invoke(this, new NodeEventArgs { Type = "NodeRemoved", NodeId = kv.Key });
            });
    }
}

public class RawNode
{
    public string EndPoint;
    public DateTime LastAlive;
}

public class NodeEventArgs : EventArgs
{
    public string Type;
    public string NodeId;
}

public class BusEventArgs : EventArgs
{
    public string Channel;
    public string Message;
}
