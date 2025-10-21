using System;

namespace RSJWYFamework.Runtime
{
    
    /// <summary>
    /// TCP客户端事件基类
    /// </summary>
    public abstract class TCPClientSoketEventArgs:EventArgsBase
    {
    }
    /// <summary>
    /// 客户端状态消息
    /// </summary>
    public sealed class TCPClientStatusEventArgs : EventArgsBase
    {
        /// <summary>
        /// 客户端状态
        /// </summary>
        public NetClientStatus NetClientStatus { get; private set; }
        /// <summary>
        /// 客户端服务句柄
        /// </summary>
        public Guid ClientHandle{ get; private set; }
        /// <summary>
        /// 客户端状态消息
        /// </summary>
        /// <param name="netClientStatus">客户端状态</param>
        /// <param name="clientHandle">客户端服务句柄</param>
        public TCPClientStatusEventArgs(NetClientStatus netClientStatus, Guid clientHandle)
        {
            NetClientStatus = netClientStatus;
            ClientHandle = clientHandle;
        }
    }
    
    
    /// <summary>
    /// 接收到服务器发来的消息
    /// </summary>
    public sealed class TCPClientReceivesMsgFromServer: TCPClientSoketEventArgs
    {
        /// <summary>
        /// 使用哪个客户端收消息
        /// </summary>
        public Guid ClientHandle{ get; private set; }
        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] Data{get;private set;}

        public TCPClientReceivesMsgFromServer(byte[] data,Guid clientHandle)
        {
            ClientHandle = clientHandle;
            Data = data;
        }
    }
    
    /// <summary>
    /// 向服务器发送消息
    /// </summary>
    public sealed class TCPClientSendToServerEventArgs : TCPClientSoketEventArgs
    {
        /// <summary>
        /// 使用哪个客户端发消息
        /// </summary>
        public Guid ClientHandle{ get; private set; }
        /// <summary>
        /// 消息令牌
        /// </summary>
        public Guid MsgToken{get;private set;}
        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] Data{get;private set;}
        public TCPClientSendToServerEventArgs(Guid clientHandle, byte[] data)
        {
            ClientHandle = clientHandle;
            Data = data;
            MsgToken = Guid.NewGuid();
        }
    }
    
    /// <summary>
    /// 通过所有客户端向每一个对应的服务端发送消息
    /// <remarks>不会全部发送完后触发一次，而是每一个客户端服务发送后都会触发</remarks>
    /// </summary>
    public sealed class TCPClientToAllServerMsgEventArgs : TCPClientSoketEventArgs
    {
        public Guid MsgToken{get;private set;}
        public byte[] Data{get;private set;}
        public TCPClientToAllServerMsgEventArgs(Guid MsgToken, byte[] Data)
        {
            this.MsgToken = MsgToken;
            this.Data = Data;
        }
    }
    /// <summary>
    /// 客户端发送消息完成
    /// <remarks>如果进行群发，并不会在全部发送完成时触发，而是每一个客户端服务发送到对应的服务端口触发</remarks>
    /// </summary>
    public sealed class TCPClientSendToServerMsgCompleteEventArgs : TCPClientSoketEventArgs
    {
        public Guid ClientHandle{get;private set;}
        public Guid MsgToken{get;private set;}
        public TCPClientSendToServerMsgCompleteEventArgs(Guid clientHandle, Guid msgToken)
        {
            ClientHandle = clientHandle;
            MsgToken = msgToken;
        }
    }
}