using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 服务器模块 单独客户端容器，存储客户端的相关信息等
    /// </summary>
    internal class ClientSocketContainer
    {
        /// <summary>
        /// 心跳包维持记录
        /// </summary>
        public long lastPingTime;

        /// <summary>  
        /// 客户端IP地址  
        /// </summary>  
        public IPAddress IPAddress;

        /// <summary>  
        /// 远程地址  
        /// </summary>  
        public EndPoint Remote;

        /// <summary>  
        /// 连接时间  
        /// </summary>  
        public DateTime ConnectTime;

        /// <summary>
        /// 存储连接的客户端
        /// </summary>
        internal Socket socket;
        /// <summary>
        /// 存储数据-接收到的数据暂存容器
        /// </summary>
        internal ByteArrayMemory ReadBuff;

        /// <summary>
        /// 客户端汇报的ID
        /// 作为客户端的唯一标识符，以免出现同一个客户端多链接，
        /// 需要做一个定时检查，出现重复拒绝链接以及移除已有链接，重新发起连接
        /// </summary>
        /// <returns></returns>
        public Guid TokenID;
        /// <summary>
        /// 所属的server
        /// </summary>
        internal TcpServerService ServerService;

        /// <summary>
        /// 写
        /// </summary>
        internal SocketAsyncEventArgs readSocketAsyncEA;
        /// <summary>
        /// 读
        /// </summary>
        internal SocketAsyncEventArgs writeSocketAsyncEA;
        /// <summary>
        /// 目标消息
        /// </summary>
        internal ConcurrentQueue<InternalSendToClientMsgContainer> sendQueue;

        /// <summary>
        /// 通知多线程自己跳出
        /// </summary>
        internal CancellationTokenSource cts;

        /// <summary>
        /// 消息发送线程
        /// </summary>
        internal Thread msgSendThread;
        
        /// <summary>
        /// 心跳包线程
        /// </summary>
        internal Thread PingPongThread;

        /// <summary>
        /// 消息队列发送锁
        /// </summary>
        internal ManualResetEventSlim msgSendDoneEvent;

        
        /// <summary>
        /// 关闭
        /// </summary>
        internal void Close()
        {
            try
            {
                cts?.Cancel();
                msgSendDoneEvent.Set();
                socket?.Shutdown(SocketShutdown.Both);
                socket?.Close();
                //本条数据发送完成，激活线程，继续处理下一条
            }
            catch (Exception e)
            {
                AppLogger.Warning($"客户端关闭时发生错误！{e}");
            }
        }
        /// <summary>
        /// 心跳包检测
        /// </summary>
        public void PongThread()
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(1000);//本线程可以每秒检测一次
                    //检测心跳包是否超时的计算
                    //获取当前时间
                    if (Utility.Timestamp.UnixTimestampSeconds-lastPingTime>ServerService.pingInterval)
                    {
                        ServerService.CloseClientSocket(this);
                        AppLogger.Warning($"客户端{TokenID}心跳包超时，已关闭连接");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error( $"检测心跳包时发生错误：{ex.Message}");
                    if (cts.Token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
            }
            AppLogger.Log($"客户端{TokenID}心跳包检测线程已退出");
        }
    }
    public enum SendToClientMsgType
    {
        /// <summary>
        /// 向指定服务端指定客户端发送消息
        /// </summary>
        STC,
        /// <summary>
        /// 向所有服务端连上来的所有客户端发送消息
        /// </summary>
        ASTAC,
        /// <summary>
        /// 向指定服务端所有客户端发送消息
        /// </summary>
        STAC
    }
    /// <summary>
    /// 服务器接收到的来自客户端消息容器
    /// </summary>
    public class FromTCPClientMsg
    {
        /// <summary>
        /// 消息TCPServer Handle
        /// </summary>
        public Guid TCPServerHandle { get; internal set; }
        
        /// <summary>
        /// 消息UDPClient Handle
        /// </summary>
        public Guid TCPClientHandle { get; internal set; }
        
        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] msgBytes{ get; internal set; }
        
        /// <summary>
        /// 接收是否成功
        /// </summary>
        public bool Success { get; internal set; }
        
        /// <summary>
        /// 接收失败原因
        /// </summary>
        public string Error { get; internal set; }
    }
    /// <summary>
    ///消息发送数据容器
    /// <remarks>
    /// 仅Service类内组装数据提交给对应客户端容器的发送队列使用
    /// </remarks>
    /// </summary>
    internal class InternalSendToClientMsgContainer
    {
        /// <summary>
        /// 消息目标客户端
        /// </summary>
        internal ClientSocketContainer TargetContainer;
        /// <summary>
        /// 已转换完成的消息数组
        /// </summary>
        internal ByteArrayMemory SendBytes;

        /// <summary>
        /// 消息发送类型，用于完成回调
        /// </summary>
        public SendToClientMsgType SendType;
        /// <summary>
        /// 消息发送Token，用于本机发送完成回调唯一标记
        /// </summary>
        internal Guid MsgToken;
    }
    /// <summary>
    ///消息发送数据容器
    /// </summary>
    public class SendToClientMsgContainer
    {
        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] data{ get; private set; }
        /// <summary>
        /// 消息发送Token，用于本机发送完成回调唯一标记
        /// </summary>
        public Guid MsgToken{ get; private set; }
        /// <summary>
        /// 消息服务器Handle
        /// </summary>
        public Guid ServerHandle{ get; private set; }
        
        /// <summary>
        /// 消息客户端Handle
        /// </summary>
        public Guid ClientHandle{ get; private set; }
        
        /// <summary>
        /// 消息发送类型
        /// </summary>
        public SendToClientMsgType SendType{ get; private set; }

        /// <summary>
        /// 创建指定服务端指定客户端消息
        /// </summary>
        /// <returns></returns>
        public static SendToClientMsgContainer CreateSTC(
            byte[] data,Guid serverHandle,Guid clientHandle)
        {
            var container = new SendToClientMsgContainer();
            container.data = data;
            container.MsgToken = Guid.NewGuid();
            container.ServerHandle = serverHandle;
            container.ClientHandle = clientHandle;
            container.SendType = SendToClientMsgType.STC;
            return container;
        }
        
        /// <summary>
        /// 创建向所有服务器所有客户端发送消息
        /// </summary>
        /// <returns></returns>
        public static SendToClientMsgContainer CreateASTAC(byte[] data)
        {
            var container = new SendToClientMsgContainer();
            container.data = data;
            container.MsgToken = Guid.NewGuid();
            container.ServerHandle = Guid.Empty;
            container.ClientHandle = Guid.Empty;
            container.SendType = SendToClientMsgType.ASTAC;
            return container;
        }
        
        /// <summary>
        /// 创建向指定服务端所有客户端发送消息
        /// </summary>
        /// <returns></returns>
        public static SendToClientMsgContainer CreateSTAC(byte[] data,Guid clientHandle)
        {
            var container = new SendToClientMsgContainer();
            container.data = data;
            container.ServerHandle = Guid.Empty;
            container.ClientHandle = clientHandle;
            container.MsgToken = Guid.NewGuid();
            container.SendType = SendToClientMsgType.STAC;
            return container;
        }
    }

    
    
    /// <summary>
    /// TCP服务器发送给客户端完成回调
    /// </summary>
    public class TCPServertToClientMsgCallBack
    {
        public enum SendCallBackType
        {
            Success,
            Error,
            Exception
        }
        /// <summary>
        /// 发送回调类型
        /// </summary>
        public SendCallBackType CallBackType { get; internal set; }

        /// <summary>
        /// Socket回调状态
        /// </summary>
        public SocketError SocketError { get; internal set; }
        
        /// <summary>
        /// 发送类型
        /// </summary>
        public SendToClientMsgType SendType { get; internal set; }
        /// <summary>
        /// 发送失败
        /// </summary>
        public string Error { get; internal set; }
        
        /// <summary>
        /// 消息Token
        /// </summary>
        public Guid MsgToken{ get; internal set; }
        
        /// <summary>
        /// 消息TCPServer Handle
        /// </summary>
        public Guid TCPServerHandle { get; internal set; }
        
        /// <summary>
        /// 消息UDPClient Handle
        /// </summary>
        public Guid TCPClientHandle { get; internal set; }
        
    }
}
