using System;
using System.Collections.Concurrent;
using Cysharp.Threading.Tasks;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 客户端的控制器
    /// </summary>
    [Module()]
    public class TcpClientManager : ModuleBase
    {
        private readonly ConcurrentDictionary<Guid, TcpClientService> tcpClientDic = new();
        
        
        public override void Initialize()
        {
            ModuleManager.GetModule<EventManager>().BindEvent<TCPClientSendToServerEventArgs>(ClientSendToServerMsg);
            ModuleManager.GetModule<EventManager>().BindEvent<TCPClientSendToServerEventArgs>(ClientSendToAllServerMsg);
        }

        public override void Shutdown()
        { 
            foreach (var item in tcpClientDic)
            {
                item.Value.Close();
            }
            ModuleManager.GetModule<EventManager>().BindEvent<TCPClientSendToServerEventArgs>(ClientSendToServerMsg);
            ModuleManager.GetModule<EventManager>().BindEvent<TCPClientSendToServerEventArgs>(ClientSendToAllServerMsg);
        }
        /// <summary>
        /// 客户端是否存在
        /// </summary>
        public bool IsExistClient(Guid serverHandle)
        {
            return tcpClientDic.ContainsKey(serverHandle);
        }
        
        public Guid Bind(string ip , int port,ISocketMsgBodyEncrypt socketMsgBodyEncrypt,
            bool isDebugPingPong = true,int bufferSize = 10485760)
        {
            try
            {
                if (!Utility.SocketTool.MatchIP(ip) || !Utility.SocketTool.MatchPort(port))
                {
                    AppLogger.Error($"无效的地址: {ip}:{port}");
                    return Guid.Empty;
                }
                var handle = Guid.NewGuid();
                var service = new TcpClientService(ip, port, this, handle, socketMsgBodyEncrypt,
                    isDebugPingPong,bufferSize);
                service.Connect();
                if (!tcpClientDic.TryAdd(handle,service))
                {
                    service.Close();
                    AppLogger.Error($"Handle冲突: {handle}");
                    return Guid.Empty;
                }
                return handle;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Bind失败: {ex.Message}");
                return Guid.Empty;
            }
        }
        
        public void UnBind(Guid clientHandle)
        {
            if (tcpClientDic.TryGetValue(clientHandle, out var service))
            {
                service.Close();
                tcpClientDic.TryRemove(clientHandle, out service);
            }
            else
            {
                AppLogger.Error($"要关闭的客户端Handle不存在: {clientHandle}");
            }
        }
        
        /// <summary>
        /// 客户端状态变更
        /// </summary>
        internal void ClientStatus(NetClientStatus netClientStatus,Guid clientHandle)
        {
            var _event= new TCPClientStatusEventArgs(netClientStatus, clientHandle);
            _event.Sender = this;
            ModuleManager.GetModule<EventManager>().Fire(_event);
        }

        /// <summary>
        /// 接收服务器消息
        /// </summary>
        /// <param name="msgBase"></param>
        /// <param name="clientHandle"></param>
        internal void ReceiveFromServerMsgCallBack(byte[] msgBase,Guid clientHandle)
        {
            var _event= new TCPClientReceivesMsgFromServer(msgBase, clientHandle);
            _event.Sender = this;
            ModuleManager.GetModule<EventManager>().Fire(_event);
        }
        /// <summary>
        /// 向所有服务器发送消息
        /// </summary>
        public void ClientSendToAllServerMsg(object sender, EventArgsBase eventArgsBase)
        {
            if (eventArgsBase is TCPClientToAllServerMsgEventArgs args)
            {
                foreach (var item in tcpClientDic)
                {
                    item.Value.SendMessage(args.Data,item.Key);
                }
            }
        }
        /// <summary>
        /// 客户端发送消息
        /// </summary>
        public void ClientSendToServerMsg(object sender, EventArgsBase eventArgsBase)
        {
            if (eventArgsBase is TCPClientSendToServerEventArgs args)
                ClientSendToServerMsg(args.ClientHandle, args.MsgToken,args.Data);
        }
        /// <summary>
        /// 客户端发送消息
        /// </summary>
        public void ClientSendToServerMsg(Guid clientHandle, Guid msgToken,byte[] data)
        {
            if (tcpClientDic.TryGetValue(clientHandle, out var service))
            {
                service.SendMessage(data,clientHandle);
            }
            else
            {
                AppLogger.Warning($"向服务器发送消息，客户端{clientHandle}不存在");
            }
        }

        /// <summary>
        /// 客户端发送消息完成回调通知
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <param name="msgToken"></param>
        internal void ClientSendToServerMsgCompleteCallBack(Guid clientHandle, Guid msgToken)
        {
            var _event= new TCPClientSendToServerMsgCompleteEventArgs(clientHandle, msgToken);
            _event.Sender = this;
            ModuleManager.GetModule<EventManager>().Fire(_event);
        }


        public override void LifePerSecondUpdate()
        {
            foreach (var item in tcpClientDic)
            {
                if (item.Value?.Status==NetClientStatus.Close||item.Value?.Status==NetClientStatus.Fail||item.Value?.Status==NetClientStatus.ConnectFail)
                {
                    AppLogger.Warning($"检测到服务器链接关闭，重新连接服务器");
                    item.Value.ReConnectToServer();
                }
            }
        }
    }
}

