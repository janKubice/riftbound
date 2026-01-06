using UnityEngine;

public enum LobbyPrivacy
{
    Public,
    FriendsOnly,
    Private
}

public enum CharacterType
{
    Warrior, // ID 0
    Mage,    // ID 1
    Rogue,    // ID 2
    Healer,
    Guy
}

// Struktura pro data hráče v lobby
public struct LobbyPlayerData : Unity.Netcode.INetworkSerializable, System.IEquatable<LobbyPlayerData>
{
    public ulong ClientId;
    public Unity.Collections.FixedString64Bytes PlayerName;
    public int CharacterId;
    public bool IsReady;

    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
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