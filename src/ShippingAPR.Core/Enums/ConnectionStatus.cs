namespace ShippingAPR.Core.Enums;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error,
    /// <summary>Permanent failure after all reconnection attempts exhausted.</summary>
    Failed
}
