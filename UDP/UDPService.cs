using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace RSJWYFamework.Runtime
{
    internal class UDPService
    {
        /// <summary>
        /// 监听端口
        /// </summary>
        private int _port;
        /// <summary>
        /// 监听IP
        /// </summary>
        IPAddress _ip ;
        
         IPEndPoint ipendpoint;
        /// <summary>
        /// UDP Socket
        /// </summary>
        System.Net.Sockets.Socket _udpClient;
        /// <summary>
        /// 消息发送队列
        /// </summary>
        ConcurrentQueue<UDPSendMsg> _SendMsgQueue = new ();
        
        /*
        /// <summary>
        /// 接收到的消息队列
        /// </summary>
        ConcurrentQueue<UDPReciveMsg> _ReciveMsgQueue = new ();*/
        /// <summary>
        /// 消息发送线程
        /// </summary>
        Thread _sendMsgThread;
        /// <summary>
        ///  消息队列发送锁
        /// </summary>
        
        ManualResetEventSlim msgSendDoneEvent = new ManualResetEventSlim(false);
        /// <summary>
        /// 通知多线程自己跳出
        /// </summary>
        private static CancellationTokenSource _cts;

        /// <summary>
        /// 写
        /// </summary>
        private SocketAsyncEventArgs _read;
        /// <summary>
        /// 读
        /// </summary>
        private SocketAsyncEventArgs _write;
        
        /// <summary>
        /// UDP管理器
        /// </summary>
        private UDPManager _udpManager;
        
        /// <summary>
        /// 服务句柄
        /// </summary>
        private Guid _handle;
        
        /// <summary>
        /// 是否支持广播
        /// </summary>
        private bool _enableBroadcast;

        private int _bufferSize = 1024 * 1024;
        internal UDPService(string ip, int port,UDPManager udpManager, Guid handle,int bufferSize=1024*1024,bool enableBroadcast=true)
        {
            this._ip = IPAddress.Parse(ip);
            this._port = port;
            this._udpManager = udpManager;
            this._handle = handle;
            this._enableBroadcast = enableBroadcast;
            this._bufferSize = bufferSize;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns>true成功监听了或者初始化成功过一次了</returns>
        internal void  Bind()
        {
            try
            {
                _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                //支持广播消息
                _udpClient.EnableBroadcast = _enableBroadcast;
                //配置监听
                ipendpoint = new IPEndPoint(_ip, _port);
                _udpClient.Bind(ipendpoint);
                _cts = new CancellationTokenSource();
                //开启异步监听
                _sendMsgThread = new Thread(SendMsgThread);
                _sendMsgThread.IsBackground = true;//后台运行
                _sendMsgThread.Start();
                AppLogger.Log($"UDP启动监听 {_udpClient.LocalEndPoint.ToString()} ");
                Start();
            }
            catch (Exception e)
            {
                AppLogger.Error($"UDP启动监听 {_udpClient.LocalEndPoint.ToString()} 失败！！错误信息：\n {e.ToString()}");
            }
        }
        /// <summary>
        /// 开启接收
        /// </summary>
        private void Start()
        {
            _read = new SocketAsyncEventArgs();
            _read.SetBuffer(new byte[_bufferSize], 0, _bufferSize);
            _read.RemoteEndPoint = ipendpoint;
            _read.Completed += IO_Completed; 
            
            _write = new SocketAsyncEventArgs();
            _write.Completed += IO_Completed;
            
            //当为false时，需手动调用回调
            if (!_udpClient.ReceiveFromAsync(_read))
                Task.Run(() => ProcessReceived(_read));
        }
        /// <summary>
        /// 发送/接收共用回调
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // SocketAsyncEventArgs回调处理
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.ReceiveFrom:
                    Task.Run(() => ProcessReceived(e));
                    break;
                case SocketAsyncOperation.SendTo:
                    Task.Run(() => ProcessSend(e));
                    break;
                default:
                    AppLogger.Warning($"UDP IO_Completed 在套接字上完成的最后一个操作不是接收或发送，{e.LastOperation}");
                    break;
            }
        }
        /// <summary>
        /// UDP消息接收
        /// </summary>
        /// <param name="e"></param>
        private void ProcessReceived(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    byte[] data = new byte[e.BytesTransferred];
                    Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesTransferred);
                    var msg = new UDPReciveMsg
                    {
                        Success = true,
                        Bytes = data,
                        remoteEndPoint = e.RemoteEndPoint as IPEndPoint,
                        Error = string.Empty,
                        UDPServerHandle = _handle
                    };
                    _udpManager.ReciveMsgCallBack(msg);
                }
                else
                {
                    AppLogger.Error($"UDP 接收时发生非成功时错误！: {e.SocketError}");
                    var msg = new UDPReciveMsg
                    {
                        Success=false,
                        remoteEndPoint = e.RemoteEndPoint as IPEndPoint,
                        Error=e.SocketError.ToString(),
                        UDPServerHandle = _handle
                    };
                    _udpManager.ReciveMsgCallBack(msg);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Error($"UDP 接收时发生异常错误: {exception}");
                var msg = new UDPReciveMsg
                {
                    Success=false,
                    remoteEndPoint = e.RemoteEndPoint as IPEndPoint,
                    Error=exception.ToString(),
                    UDPServerHandle = _handle
                };
                _udpManager.ReciveMsgCallBack(msg);
            }
            finally
            {
                //无论是否异常，重设缓冲区，接收下一组数据
                e.SetBuffer(0,e.Buffer.Length);
                if (!_udpClient.ReceiveFromAsync(e))
                    Task.Run(() => ProcessReceived(e));
            }
        }
        /// <summary>
        /// 发送UDP消息
        /// </summary>
        public void SendUdpMessage(UDPSendMsg udpSendMsg)
        {
            if (_udpClient == null)
            {
                AppLogger.Warning("UDP服务未初始化或已关闭，无法发送消息。");
            }
            // 发送数据
            _SendMsgQueue.Enqueue(udpSendMsg);
        }
        /// <summary>
        /// 消息发送完成回调
        /// </summary>
        /// <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            //不管是否发送成功，都从队列移除，并通知线程继续发送下一个消息
            _SendMsgQueue.TryDequeue(out var _msg);
            msgSendDoneEvent.Set();
            // 处理发送完成的逻辑
            if (e.SocketError == SocketError.Success)
            {
                _udpManager.SendMsgCallBack(new UDPSendCallBack()
                {
                    Success = true,
                    Error = string.Empty,
                    MsgToken = _msg.MsgToken,
                    UDPServerHandle = _handle
                });
            }
            else
            {
                AppLogger.Error($"UDP 发送时发生错误，socker状态为：{e.SocketError}");
                _udpManager.SendMsgCallBack(new UDPSendCallBack()
                {
                    Success = false,
                    Error = e.SocketError.ToString(),
                    MsgToken = _msg.MsgToken,
                    UDPServerHandle = _handle
                });
            }
            
        }
        /// <summary>
        /// 消息发送监控线程
        /// </summary>
        void SendMsgThread()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (_SendMsgQueue.Count <= 0)
                    {
                        //短暂休眠，避免CPU高占用
                        Thread.Sleep(100);
                        continue;
                    }
                    //取出数据
                    UDPSendMsg _data;
                    //取出不移除
                    _SendMsgQueue.TryPeek(out _data);
                    //绑定消息
                    _write.SetBuffer(_data.data, 0, _data.data.Length);
                    _write.RemoteEndPoint = _data.remoteEndPoint;
                    msgSendDoneEvent.Reset();
                    if (!_udpClient.SendToAsync(_write))
                        Task.Run(() => ProcessSend(_write));
                    msgSendDoneEvent.Wait(TimeSpan.FromSeconds(20));
                }
                catch (Exception e)
                {
                    AppLogger.Error($"SendMsgThread 线程处理异常！！：{e}");
                    // 重启线程
                    Thread.Sleep(1000); // 等待一段时间再重启，避免立即重启可能导致的问题
                    if (_cts.IsCancellationRequested)
                    {
                        AppLogger.Warning($"请求取消任务");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 关闭监听
        /// </summary>
        public void Close()
        {
            _cts?.Cancel();
            msgSendDoneEvent.Set();
            _udpClient?.Close();
        }

    }
}
