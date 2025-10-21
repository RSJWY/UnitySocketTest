namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// UDP基本广播事件
    /// </summary>
    public abstract class UDPSoketBaseEventArgs:EventArgsBase
    {
                
    }
    /// <summary>
    /// UDP接收消息事件
    /// </summary>
    public sealed class UDPSoketReciveMsgEventArgs: UDPSoketBaseEventArgs
    {
        public UDPReciveMsg UDPReciveMsg{get;internal set;}
    }
    /// <summary>
    /// UDP发送消息回调事件
    /// </summary>
    public sealed class UDPSoketSendCallBackEventArgs: UDPSoketBaseEventArgs
    {
        public UDPSendCallBack UDPSendCallBack{get;internal set;}
    }
    /// <summary>
    /// UDP发送消息事件
    /// </summary>
    public sealed class UDPSoketSendMsgEventArgs: UDPSoketBaseEventArgs
    {
        public UDPSendMsg UDPSendMsg{get;private set;}
        public UDPSoketSendMsgEventArgs(UDPSendMsg udpSendMsg)
        {
            UDPSendMsg = udpSendMsg;
        }
    }
}