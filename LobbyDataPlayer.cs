using Unity.Netcode;
using Unity.Collections;
using System;

public struct LobbyDataPlayer : INetworkSerializable, IEquatable<LobbyPlayerData>
{
    public ulong ClientId;
    public FixedString64Bytes PlayerName; // FixedString je nutný pro NetworkList
    public int CharacterId; // 0 = Warrior, 1 = Mage, atd.
    public bool IsReady;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref CharacterId);
        serializer.SerializeValue(ref IsReady);
    }

    public bool Equals(LobbyPlayerData other)
    {
        return ClientId == other.ClientId &&
               PlayerName == other.PlayerName &&
               CharacterId == other.CharacterId &&
               IsReady == other.IsReady;
    }
}