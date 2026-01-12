using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // Umožní klientovi (Owner) odesílat pozici na server
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}