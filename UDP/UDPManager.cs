using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// UDP控制器
    /// </summary>
    [Module]
    public class UDPManager:ModuleBase
    {
        /// <summary>
        /// UDP字典
        /// </summary>
        private readonly ConcurrentDictionary<Guid, UDPService> udpServiceDic = new();

        public bool IsExistUDPService(Guid handle)
        {
            return udpServiceDic.ContainsKey(handle);
        }
        /// <summary>
        /// 创建一个UDP服务
        /// </summary>
        public Guid Bind(string ip, int port,UDPManager udpManager,int bufferSize=1024*1024,bool enableBroadcast=true) 
        {
            try 
            {
                if (!Utility.SocketTool.MatchIP(ip) || !Utility.SocketTool.MatchPort(port))
                {
                    AppLogger.Error($"无效的地址: {ip}:{port}");
                    return Guid.Empty;
                }

                var handle = Guid.NewGuid();
                var service = new UDPService(ip, port, this, handle,bufferSize,enableBroadcast);

                service.Bind();

                if (!udpServiceDic.TryAdd(handle, service)) 
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
        /// <summary>
        /// 关闭指定UDP服务
        /// </summary>
        public void CloseUDPService(Guid handle)
        {
            if (udpServiceDic.TryRemove(handle, out var service))
            {
                service.Close();
                udpServiceDic.TryRemove(handle, out service);
            }
            else
            {
                AppLogger.Error($"未找到UDP服务: {handle}");
            }
        }

        /// <summary>
        /// 关闭所有UDP服务
        /// </summary>
        public void CloseAllUDPServices()
        {
            foreach (var service in udpServiceDic.Values)
            {
                service.Close();
            }
            udpServiceDic.Clear();
        }

        /// <summary>
        /// UDP接收数据进行广播
        /// </summary>
        /// <param name="udpReciveMsg"></param>
        internal void ReciveMsgCallBack(UDPReciveMsg udpReciveMsg)
        {
            ModuleManager.GetModule<EventManager>().Fire(new UDPSoketReciveMsgEventArgs
            {
                Sender = this,
                UDPReciveMsg = udpReciveMsg
            });
        }

        /// <summary>
        /// 消息发送完成后的信息，返回是否发送成功
        /// </summary>
        /// <param name="udpSendCallBack"></param>
        internal void SendMsgCallBack(UDPSendCallBack udpSendCallBack)
        {
            ModuleManager.GetModule<EventManager>().Fire(new UDPSoketSendCallBackEventArgs
            {
                Sender = this,
                UDPSendCallBack = udpSendCallBack
            });
        }

        /// <summary>
        /// 是否存在指定的UDP服务
        /// </summary>
        public bool IsUDPServiceExist(Guid handle)
        {
            return udpServiceDic.ContainsKey(handle);
        }
        
        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="udpSendMsg"></param>
        /// <returns></returns>
        public void SendUdpMessage(UDPSendMsg udpSendMsg)
        {
            if (udpServiceDic.TryGetValue(udpSendMsg.UDPServerHandle, out var service))
            {
                service.SendUdpMessage(udpSendMsg);
            }
            else
            {
                AppLogger.Error($"未找到UDPServerHandle: {udpSendMsg.UDPServerHandle}");
            }
        }
        void OnSendMessage(object sender, EventArgsBase eventArgsBase)
        {
            if (eventArgsBase is UDPSoketSendMsgEventArgs args)
            {
                SendUdpMessage(args.UDPSendMsg);
            }
        }
        public override void Initialize()
        {
            ModuleManager.GetModule<EventManager>().BindEvent<UDPSoketSendMsgEventArgs>(OnSendMessage);
        }
        
        public override void Shutdown()
        {
            ModuleManager.GetModule<EventManager>().UnBindEvent<UDPSoketSendMsgEventArgs>(OnSendMessage);
            CloseAllUDPServices();
        }

        public override void LifeUpdate()
        {
        }
    }
    
}