using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Bus
{
    public event EventHandler<BusEventArgs> BusEvent;
    public event EventHandler<MessageEventArgs> MessageEvent;

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
                beacon.Publish($"ASJ/1\r\n{NodeId}:{randomPort}", TimeSpan.FromSeconds(1));
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
        var rawMsg = $"ASJ/1\r\nId:{Guid.NewGuid()}\r\nTimestamp:{DateTime.UtcNow:o}\r\nNodeId:{NodeId}\r\n\r\n{msg}";
        var m = new NetMQMessage(2);
        m.Append(channel);
        m.Append(rawMsg);
        sendQueue.Add(m);
    }

    public void Quit()
    {
        poller.Stop();
    }

    private void Subscriber_ReceiveReady(object sender, NetMQSocketEventArgs e)
    {
        var msg = subscriber.ReceiveMultipartMessage();
        var raw = msg[1].ConvertToString().Split("\r\n\r\n");
        var header = raw[0];
        var body = raw[1];
        var split = header.Split("\r\n");
        var id = split[1].Split(new char[] { ':' }, 2)[1];
        var ts = split[2].Split(new char[] { ':' }, 2)[1];
        var nodeid = split[3].Split(new char[] { ':' }, 2)[1];
        MessageEvent?.Invoke(this, new MessageEventArgs
        {
            Channel = msg[0].ConvertToString(),
            Id = id,
            Timestamp = DateTime.Parse(ts),
            NodeId = nodeid,
            Message = body
        });
    }

    private async void Beacon_ReceiveReady(object sender, NetMQBeaconEventArgs e)
    {
        var msg = beacon.Receive();
        var parts = msg.String.Split("\r\n");
        var magic = parts[0].Substring(0, 3);
        if (magic != "ASJ") return;
        var version = parts[0].Substring(4);
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
            BusEvent?.Invoke(this, new BusEventArgs { Type = "NodeAdded", NodeId = nodeId });
        }
        else nodes[nodeInfo[0]].LastAlive = DateTime.Now;
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
                BusEvent?.Invoke(this, new BusEventArgs { Type = "NodeRemoved", NodeId = kv.Key });
            });
    }
}

public class RawNode
{
    public string EndPoint;
    public DateTime LastAlive;
}

public class BusEventArgs : EventArgs
{
    public string Type;
    public string NodeId;
}

public class MessageEventArgs : EventArgs
{
    public string Channel;
    public string Id;
    public DateTime Timestamp;
    public string NodeId;
    public string Message;
}
