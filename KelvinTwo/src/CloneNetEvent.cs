using System;
using Bolt;
using SonsSdk.Networking;
using UdpKit;

namespace KelvinTwo;

/// <summary>
/// Host → clients: "this Bolt entity is Kelvin 2". Clients only use it for the tint
/// and name tag; all gameplay effects run on the host, which simulates the AI.
/// </summary>
internal sealed class CloneNetEvent : Packets.NetEvent
{
    private const int PayloadBytes = 16;

    private readonly Action<BoltEntity> _onReceived;

    internal CloneNetEvent(Action<BoltEntity> onReceived) => _onReceived = onReceived;

    public override string Id => "KelvinTwo.CloneMark";

    internal void Broadcast(BoltEntity clone)
    {
        if (!BoltNetwork.isRunning || !BoltNetwork.isServer || clone == null || !clone.isAttached)
            return;
        var p = NewPacket(PayloadBytes, GlobalTargets.Others);
        p.Packet.WriteBoltEntity(clone);
        Send(p);
    }

    public override void Read(UdpPacket packet, BoltConnection fromConnection)
    {
        var entity = packet.ReadBoltEntity();
        if (entity != null)
            _onReceived(entity);
    }
}
