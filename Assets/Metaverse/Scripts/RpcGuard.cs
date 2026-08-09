using Unity.Netcode;

/// <summary>The check every SendTo.Server RPC opens with: reject anyone but the object's own owner.</summary>
public static class RpcGuard
{
    public static bool IsFromOwner(this NetworkBehaviour behaviour, RpcParams rpcParams)
    {
        return rpcParams.Receive.SenderClientId == behaviour.OwnerClientId;
    }
}
