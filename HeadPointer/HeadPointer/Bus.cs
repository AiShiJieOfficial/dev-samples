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

    public string NodeId { get; }
    public List<string> AllNodeIds => nodes.Keys.ToList();

    NetMQBeacon beacon;
    NetMQPoller poller;
    PublisherSocket publisher;
    SubscriberSocket subscriber;
    NetMQTimer timer;

    readonly Dictionary<string, RawNode> nodes;
    readonly BlockingCollection<NetMQMessage> sendQueue;

    public Bus()
    {
        NodeId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        nodes = new Dictionary<string, RawNode>();
        sendQueue = new BlockingCollection<NetMQMessage>();
    }

    int randomPort;
    Action sendJob;
    public void Init(int udpPort)
    {
        Task.Run(() =>
        {
            timer = new NetMQTimer(TimeSpan.FromSeconds(1));
            timer.Elapsed += Timer_Elapsed;

            using (subscriber = new SubscriberSocket())
            using (publisher = new PublisherSocket())
            using (beacon = new NetMQBeacon())
            using (poller = new NetMQPoller { subscriber, beacon, timer })
            {
                subscriber.Subscribe("Public");
                subscriber.Subscribe(NodeId);
                subscriber.ReceiveReady += Subscriber_ReceiveReady;

                randomPort = publisher.BindRandomPort("tcp://*");

                beacon.Configure(udpPort);
                beacon.Publish($"ASJ1 {NodeId}:{randomPort}", TimeSpan.FromSeconds(1));
                beacon.Subscribe("");
                beacon.ReceiveReady += Beacon_ReceiveReady;

                poller.Run();
            }
        });
    }

    public void Subscribe(string channel)
    {
        subscriber?.Subscribe(channel);
    }

    public void Send(string channel, string msg)
    {
        if (sendJob == null)
        {
            sendJob = () =>
            {
                while (true)
                {
                    publisher?.SendMultipartMessage(sendQueue.Take());
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
        poller.Stop();
    }

    private void Subscriber_ReceiveReady(object sender, NetMQSocketEventArgs e)
    {
        var msg = subscriber.ReceiveMultipartMessage();
        BusEvent?.Invoke(this, new BusEventArgs { Channel = msg[0].ConvertToString(), Message = msg[1].ConvertToString() });
    }

    private async void Beacon_ReceiveReady(object sender, NetMQBeaconEventArgs e)
    {
        var msg = beacon.Receive();
        var parts = msg.String.Split(' ');
        var magic = parts[0].Substring(0, 3);
        if (magic != "ASJ") return;
        var version = parts[0].Substring(3);
        if (version != "1") return;

        var nodeInfo = parts[1].Split(':');
        var nodeId = nodeInfo[0];
        int.TryParse(nodeInfo[1], out int port);

        var endPoint = $"tcp://{msg.PeerHost}:{port}";
        if (!nodes.ContainsKey(nodeId))
        {
            nodes[nodeId] = new RawNode { EndPoint = endPoint, LastAlive = DateTime.Now };
            subscriber.Connect(endPoint);
            await Task.Delay(1500);
            NodeEvent?.Invoke(this, new NodeEventArgs { Type = "NodeAdded", NodeId = nodeId });
        }
        else nodes[nodeId].LastAlive = DateTime.Now;
    }

    private void Timer_Elapsed(object sender, NetMQTimerEventArgs e)
    {
        nodes
            .Where(n => DateTime.Now > n.Value.LastAlive + TimeSpan.FromSeconds(5))
            .ToList()
            .ForEach(kv =>
            {
                nodes.Remove(kv.Key);
                subscriber.Disconnect(kv.Value.EndPoint);
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
