using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 网络状态
    /// </summary>
    public enum NetClientStatus
    {
        None,
        /// <summary>
        /// 连接中
        /// </summary>
        Connecting,
        /// <summary>
        /// 连接成功
        /// </summary>
        Connect,
        /// <summary>
        /// 连接服务器失败
        /// </summary>
        ConnectFail,
        /// <summary>
        /// 关闭服务器中
        /// </summary>
        Closing,
        /// <summary>
        /// 已关闭和服务器的链接
        /// </summary>
        Close,
        /// <summary>
        /// 服务器断开
        /// </summary>
        Disconnect,
        /// <summary>
        /// 发生错误，无法链接，参数设置问题
        /// </summary>
        Fail
    }
    /// <summary>
    ///TCP 客户端服务
    /// </summary>
    internal sealed class TcpClientService
    {
        /// <summary>
        /// 状态
        /// </summary>
        private NetClientStatus _status;
        /// <summary>
        /// 状态-变更时广播事件
        /// </summary>
        public NetClientStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    SocketTcpClientManager.ClientStatus(_status,ClientHandle);
                }
            }
        }
        
        
        /// <summary>
        /// 消息体加解接口
        /// </summary>
        internal ISocketMsgBodyEncrypt  m_MsgBodyEncrypt;
        
        /// <summary>
        /// 本机连接的socket
        /// </summary>
        Socket _socket;
        /// <summary>
        /// 数据
        /// </summary>
        ByteArrayMemory _readBuff;
        /// <summary>
        /// IP
        /// </summary>
        string _ip;
        /// <summary>
        /// 端口
        /// </summary>
        int _port;

        /// <summary>
        /// 消息处理线程
        /// </summary>
        Thread m_msgThread;
        /// <summary>
        /// 心跳包线程
        /// </summary>
        Thread m_HeartThread;
        /// <summary>
        /// 消息队列发送监控线程
        /// </summary>
        Thread msgSendThread;
        /// <summary>
        ///  消息队列发送锁
        /// </summary>
        ManualResetEventSlim msgSendDoneEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// 最后一次发送心跳包时间
        /// </summary>
        long lastPingTime;
        /// <summary>
        /// 最后一次接收到心跳包的时间
        /// </summary>
        long lastPongTime;
        /// <summary>
        /// 心跳包间隔时间
        /// </summary>
        long m_PingInterval = 5;
       

        /// <summary>
        /// 消息发送队列
        /// </summary>
        ConcurrentQueue<SendClientToServerMsg> m_WriteQueue;

        /// <summary>
        /// 接收的MsgBase——要处理的消息
        /// </summary>
        ConcurrentQueue<byte[]> FromServerMsgQueue;
        
        /// <summary>
        /// 接收数据
        /// </summary>
        SocketAsyncEventArgs _ReadsocketAsyncEventArgs;
        
        /// <summary>
        /// 发送数据
        /// </summary>
        SocketAsyncEventArgs _WritesocketAsyncEventArgs;
        
        /// <summary>
        /// 记录当前网络
        /// </summary>
        NetworkReachability m_CurNetWork = NetworkReachability.NotReachable;
        
        /// <summary>
        /// 通知多线程自己跳出
        /// </summary>
        private CancellationTokenSource cts;

        /// <summary>
        /// 绑定的客户端控制器，便于回调
        /// </summary>
        internal TcpClientManager SocketTcpClientManager;
        
        /// <summary>
        /// 客户端服务句柄
        /// </summary>
        private Guid ClientHandle;
        
        /// <summary>
        /// 是否开启调试心跳包
        /// </summary>
        private bool _isDebugPingPong = false;
        /// <summary>
        /// 数据缓冲区
        /// </summary>
        private int _bufferSize = 10485760;

        public TcpClientService(string ip, int port, TcpClientManager socketTcpClientManager,
            Guid clientHandle,ISocketMsgBodyEncrypt socketMsgBodyEncrypt,
            bool isDebugPingPong = false,int bufferSize = 10485760)
        {
            _ip = ip;
            _port = port;
            SocketTcpClientManager = socketTcpClientManager;
            ClientHandle = clientHandle;
            m_MsgBodyEncrypt = socketMsgBodyEncrypt;
            _isDebugPingPong = isDebugPingPong;
            _bufferSize = bufferSize;
        }

        #region 连接服务器
        
        public void Connect()
        {
            Connect(_ip, _port);
           
        }
        
        
        /// <summary>
        /// 初始状态，初始化变量
        /// </summary>
        void InitState()
        {
            cts?.Cancel();
            cts = new();
            _socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);//配置连接模式
            _readBuff = new ByteArrayMemory();//开信息收数组
            m_WriteQueue = new ConcurrentQueue<SendClientToServerMsg>();//消息发送队列
            FromServerMsgQueue = new();//接收消息处理队列（所有接收到要处理的消息
            Status = NetClientStatus.None;
        }

        /// <summary>
        /// 链接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        internal void Connect(string ip, int port)
        {
            _ip = ip;
            _port = port;
            //链接不为空并且链接成功
            if (_socket != null && _socket.Connected)
            {
                AppLogger.Warning("链接失败，已经有链接服务器");
                return;
            }
            InitState();//初始
            _socket.NoDelay = true;//没有延时，写入数据时立即发送
            
            //执行连接
            StartAConnect(null);
        }

        /// <summary>
        /// 开始配置连接信息
        /// </summary>
        /// <param name="eventArg"></param>
        void StartAConnect(SocketAsyncEventArgs eventArg)
        {
            if (eventArg == null)  
            {  
                //没有，创建一个新的
                eventArg = new SocketAsyncEventArgs();  
                eventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(_ip), _port); // 修改为服务器的IP地址和端口
                eventArg.Completed += (sender, e) =>
                {
                    Task.Run(() => ProcessConnect(e));
                };  
            } 
            Status = NetClientStatus.Connecting;
            try
            {
                //绑定时必须检查，绑定时已完成不会触发回调
                //https://learn.microsoft.com/zh-cn/dotnet/api/system.net.sockets.socket.acceptasync?view=netframework-4.8.1#system-net-sockets-socket-acceptasync(system-net-sockets-socketasynceventargs)
                if (!_socket.ConnectAsync(eventArg))
                    Task.Run(() => ProcessConnect(eventArg));
                //输出日志
                AppLogger.Log($"客户端消息：开始链接服务器，目标：IP:{_ip}，Port：{_port}");
            }
            catch (Exception e)
            {
                Status = NetClientStatus.ConnectFail;
                AppLogger.Error($" StartAConnect 连接服务器时发生异常，疑似\n {e}");
            }
        }
        /// <summary>
        /// 连接的回调
        /// </summary>
        /// <param name="socketAsyncEArgs">用于连接</param>
        void ProcessConnect(SocketAsyncEventArgs socketAsyncEArgs)
        {
            try
            {
                if (socketAsyncEArgs.SocketError==SocketError.Success)
                {
                    _ReadsocketAsyncEventArgs = new SocketAsyncEventArgs();
                    _WritesocketAsyncEventArgs=new SocketAsyncEventArgs();
                
                    //心跳包时间初始化
                    lastPingTime = Utility.Timestamp.UnixTimestampSeconds;
                    lastPongTime =  Utility.Timestamp.UnixTimestampSeconds;

                    //配置socketAsyncEArgs
                    //注意！！！count参数必须设置！
                    _ReadsocketAsyncEventArgs.SetBuffer(new byte[_bufferSize],0,_bufferSize);
                    _ReadsocketAsyncEventArgs.Completed += IO_Completed;
                    _WritesocketAsyncEventArgs.Completed += IO_Completed;
                
                    //创建消息处理线程-后台处理
                    m_msgThread = new Thread(() =>FromMsgThread(cts.Token))
                    {
                        IsBackground = true //设置为后台可运行
                    };
                    m_msgThread.Start();//启动线程
                
                    //心跳包线程-后台处理
                    m_HeartThread = new Thread(() =>PingThread(cts.Token))
                    {
                        IsBackground = true //后台运行
                    };
                    m_HeartThread.Start();

                    //消息发送线程
                    msgSendThread = new Thread(() =>MsgSendListenThread(cts.Token))
                    {
                        IsBackground = true
                    };
                    msgSendThread.Start();
                    //已连接
                    Status = NetClientStatus.Connect;
                    
                    AppLogger.Log($"客户端消息：和服务器连接成功！ 成功连接上服务器！Socket：{_socket.RemoteEndPoint.ToString()}");
                    //接收数据
                    if (!_socket.ReceiveAsync(_ReadsocketAsyncEventArgs)) 
                        Task.Run(() => ProcessReceive(_ReadsocketAsyncEventArgs)); // 异步执行接收处理，避免递归调用
                }
                else
                {
                    Status = NetClientStatus.ConnectFail;
                    AppLogger.Warning($"ProcessConnect 未能和服务器完成链接，SocketError：{socketAsyncEArgs.SocketError}");
                }
            }
            catch (Exception ex)
            {
                Status = NetClientStatus.ConnectFail;
                AppLogger.Warning($"ProcessConnect 处理连接后回调错误:{ex.ToString()}");
            }
        }
        #endregion

        #region 消息处理
        /// <summary>
        /// SocketAsyncEventArgs的操作回调
        /// </summary>
        void IO_Completed(object sender, SocketAsyncEventArgs e)  
        {  
            // SocketAsyncEventArgs回调处理
            switch (e.LastOperation)  
            {  
                case SocketAsyncOperation.Receive:  
                    Task.Run(() => ProcessReceive(e));  
                    break;  
                case SocketAsyncOperation.Send:  
                    Task.Run(() =>ProcessSend(e));  
                    break;  
                default:  
                    AppLogger.Warning($"在套接字上完成的最后一个操作不是接收或发送，{e.LastOperation}");  
                    break;
            }  
        }
        /// <summary>
        /// 消息接收回调
        /// </summary>
        /// <param name="socketAsyncEventArgs"></param>
        private void ProcessReceive(SocketAsyncEventArgs socketAsyncEventArgs)
        {
            if (!_socket.Connected)
            {
                AppLogger.Warning($"客户端消息：和服务器的连接关闭中,或服Socket连接状态为False，不执行消息处理回调");
                return;
            }
            try
            {
                //主要是满足BytesTransferred>0时，
                //SocketError.Success && socketAsyncEventArgs.BytesTransferred == 0是服务器主动调用断开
                if (socketAsyncEventArgs.SocketError == SocketError.Success && socketAsyncEventArgs.BytesTransferred > 0)
                {
                    //首先判断客户端token缓冲区剩余空间是否支持数据拷贝
                    if (_readBuff.Remain <= socketAsyncEventArgs.BytesTransferred)
                        _readBuff.ReSize(_readBuff.WriteIndex + socketAsyncEventArgs.BytesTransferred);
                    lock (_readBuff)
                    {
                        _readBuff.SetBytes(socketAsyncEventArgs.Buffer, socketAsyncEventArgs.Offset,socketAsyncEventArgs.BytesTransferred);
                        socketAsyncEventArgs.SetBuffer(0, socketAsyncEventArgs.Buffer.Length);
                        while (_readBuff.Readable > 4)
                        {
                            //获取消息长度
                            int msgLength = BitConverter.ToInt32(_readBuff.GetlengthBytes(4).ToArray());
                            //判断是不是分包数据
                            if (_readBuff.Readable < msgLength + 4)
                            {
                                //如果消息长度小于读出来的消息长度
                                //此为分包，不包含完整数据
                                //因为产生了分包，可能容量不足，根据目标大小进行扩容到接收完整
                                //扩容后，retun，继续接收
                                _readBuff.MoveBytes(); //已经完成一轮解析，移动数据
                                _readBuff.ReSize(msgLength + 8); //扩容，扩容的同时，保证长度信息也能被存入
                                // 注意要再次绑定监听再离开以继续接收
                                if (!_socket.ReceiveAsync(socketAsyncEventArgs))
                                    Task.Run(() => ProcessReceive(socketAsyncEventArgs));
                                return;
                            }

                            //移动，规避长度位，从整体消息开始位接收数据
                            _readBuff.ReadIndex += 4; //前四位存储字节流数组长度信息
                            //在消息接收异步线程内同步处理消息，保证当前客户消息顺序性
                            var _msgBase = Utility.TCPSocketTool.DecodeMsg(_readBuff.GetlengthBytes(msgLength),m_MsgBodyEncrypt);
                            if (_msgBase.IsPingPong)
                            {
                                //写心跳信息
                                //更新接收到的心跳包时间（后台运行）
                                lastPongTime = Utility.Timestamp.UnixTimestampSeconds;
                                if (_isDebugPingPong)
                                    AppLogger.Log($"<color=blue>接收到服务器心跳包</color>");
                            }
                            else
                            {
                                //处理消息
                                FromServerMsgQueue.Enqueue(_msgBase.msgBase);
                            }
                            //处理完后移动数据位
                            _readBuff.ReadIndex += msgLength;
                            //检查是否需要扩容
                            _readBuff.CheckAndMoveBytes();
                            //结束本次处理循环，如果粘包，下一个循环将会处理
                        }
                    }
                    //继续接收. 为什么要这么写,请看Socket.ReceiveAsync方法的说明  
                    if (!_socket.ReceiveAsync(socketAsyncEventArgs))
                        Task.Run(() => ProcessReceive(socketAsyncEventArgs));
                }
                else
                {
                    //
                    AppLogger.Warning($"ProcessReceive 接收服务器消息失败：SocketError：{socketAsyncEventArgs.SocketError},Socket BytesTransferred:{socketAsyncEventArgs.BytesTransferred},");
                    Status = NetClientStatus.Disconnect;
                    Close();
                    //服务器关闭链接
                    return;
                }
            }
            catch (Exception e)
            {
                AppLogger.Warning($"获取服务器发来的信息时发生错误！！错误信息：{e.ToString()}" );
                Close();
            }
        }

        /// <summary>
        /// 消息发送回调
        /// </summary>
        /// <param name="socketAsyncEventArgs"></param>
        private void ProcessSend(SocketAsyncEventArgs socketAsyncEventArgs)
        {
            try
            {
                if (socketAsyncEventArgs.SocketError==SocketError.Success&&socketAsyncEventArgs.BytesTransferred > 0)
                {
                    m_WriteQueue.TryPeek(out var ba);
                    ba.Data.ReadIndex += socketAsyncEventArgs.BytesTransferred; //记录已发送索引
                    //发送不完整，再次发送
                    if (ba.Data.Readable >0)
                    {
                        //重新获取可用数据切片作为发送数据
                        socketAsyncEventArgs.SetBuffer(ba.Data.GetRemainingSlices());
                        if (!_socket.SendAsync(socketAsyncEventArgs))
                            Task.Run(() => ProcessSend(socketAsyncEventArgs));
                    }
                    else
                    {
                        m_WriteQueue.TryDequeue(out var _bDelete);//取出但不使用，只为了从队列中移除
                        if (ba.MsgToken!=Guid.Empty)
                        {
                            SocketTcpClientManager.ClientSendToServerMsgCompleteCallBack(ClientHandle,ba.MsgToken);
                        }
                        ba = null;//发送完成，置空
                        //本条数据发送完成，激活线程，继续处理下一条
                        // 🔔 唤醒发送线程
                        msgSendDoneEvent.Set();

                    }
                }
                else
                {
                    AppLogger.Error($"向服务器发送消息失败 ProcessSend Error:SocketErrorCode：{socketAsyncEventArgs.SocketError},BytesTransferred:{socketAsyncEventArgs.BytesTransferred}" );
                    Close();
                }
                
            }
            catch (SocketException ex)
            {
                AppLogger.Error($"向服务器发送消息失败 ProcessSend Error:{ex}" );
                Close();
            }
        }
        /// <summary>
        /// 发送信息到服务器
        /// </summary>
        /// <param name="msgBase"></param>
        internal void SendMessage(byte[] msgBase,Guid msgToken)
        {
            if (_socket == null || !_socket.Connected)
            {
                AppLogger.Warning($"{ClientHandle}：没有连接到服务器");
                return;//链接不存在或者未建立链接
            }
            //写入数据
            try
            {
                ByteArrayMemory sendBytes = Utility.TCPSocketTool.EncodeMsg(msgBase,m_MsgBodyEncrypt);
                var sendToServerMsg = new SendClientToServerMsg(sendBytes,msgToken);
                //写入到队列，向服务器发送消息
                m_WriteQueue.Enqueue(sendToServerMsg);//放入队列
            }
            catch (SocketException ex)
            {
                AppLogger.Error($"{ClientHandle}:向服务器发送消息失败 SendMessage Error:{ex.ToString()}");
                Close();
            }
        }
        #endregion

        #region 线程方法

        /// <summary>
        /// 消息处理线程回调
        /// </summary>
        void FromMsgThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (FromServerMsgQueue.Count <= 0)
                    {
                        Thread.Sleep(10);
                        continue; //当前无消息，跳过进行下一次排查处理
                    }

                    //有待处理的消息
                    byte[] msgBase = null;
                    //取出并移除取出来的数据
                    if (!FromServerMsgQueue.TryDequeue(out msgBase))
                    {
                        AppLogger.Error( "客户端消息：非正常错误！取出并处理消息队列失败！！");
                        continue;
                    }

                    //处理取出来的数据
                    if (msgBase != null)
                    {
                        //其他消息交给unity消息队列处理
                        //unityMsgQueue.Enqueue(msgBase);
                        SocketTcpClientManager.ReceiveFromServerMsgCallBack(msgBase,ClientHandle);
                    }
                }
                catch (SocketException ex)
                {
                    AppLogger.Error($"消息处理失败 SendMessage Error:{ex.ToString()}");
                    if (token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
            }
            AppLogger.Log($"消息处理线程已退出");
        }

        /// <summary>
        /// 心跳包处理
        /// </summary>
        void PingThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(1000); //本线程可以每秒检测一次
                    if (Status!=NetClientStatus.Connect)
                    {
                        //正在重连，结束或者跳过？？
                        continue;
                    }

                    long timeNow = Utility.Timestamp.UnixTimestampSeconds;
                    if (timeNow - lastPingTime > m_PingInterval)
                    {
                        lastPingTime = timeNow;
                        //发送心跳包
                        var sendToServerMsg = new SendClientToServerMsg(Utility.TCPSocketTool.SendPingPong(),Guid.Empty);
                        m_WriteQueue.Enqueue(sendToServerMsg);
                        //规定时间到，发送心跳包到服务器
                        if (_isDebugPingPong)
                            AppLogger.Log($"<color=green>向服务器发送心跳包</color>");
                    }
                    //如果心跳包过长时间没收到，关闭链接
                    if (timeNow - lastPongTime > m_PingInterval * 5)
                    {
                        AppLogger.Warning("服务器返回心跳包超时");
                        Close();
                    }
                }
                catch (SocketException ex)
                {
                    AppLogger.Error($"心跳包处理失败 SendMessage Error:{ex.ToString()}");
                    if (token.IsCancellationRequested)
                    {
                        AppLogger.Warning($"请求取消任务");
                        break;
                    }
                }
            }
            AppLogger.Log($"心跳包检测线程已退出");
        }
        /// <summary>
        /// 消息队列发送监控线程
        /// </summary>
        void MsgSendListenThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (m_WriteQueue.Count <= 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    //队列里有消息等待发送
                    //取出消息队列内的消息，但不移除队列，以获取目标客户端
                    m_WriteQueue.TryPeek(out var sendMsg);
                    //当前线程执行休眠，等待消息发送完成后继续
                    _WritesocketAsyncEventArgs.SetBuffer(sendMsg.Data.GetRemainingSlices());
                    msgSendDoneEvent.Reset(); // 🔁 先重置等待状态
                    
                    if (!_socket.SendAsync( _WritesocketAsyncEventArgs))
                        Task.Run(()=>ProcessSend(_WritesocketAsyncEventArgs));
                    // 🕒 等待发送完成，支持超时（可选）
                    msgSendDoneEvent.Wait(TimeSpan.FromSeconds(20));
                    
                    if (token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
                catch (SocketException ex)
                {
                    AppLogger.Error($"向服务器发送消息失败 SendMessage Error:{ex.ToString()}");
                    if (token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
            }
            AppLogger.Log($"消息发送线程已退出");
        }

        #endregion

        #region 关闭连接
        /// <summary>
        /// 关闭链接
        /// </summary>
        public void Close()
        {
            if (_socket == null )
            {
                //不存在
                return;
            }
            if (Status!=NetClientStatus.Closing)
                Status = NetClientStatus.Closing;
            else 
                return;
            RealClose();
        }
        /// <summary>
        /// 最终关闭链接处理
        /// </summary>
        /// <param name="normal"></param>
        void RealClose()
        {
            cts?.Cancel();
            _socket?.Close();
            // 🔔 唤醒发送线程
            msgSendDoneEvent.Set();
            /*lock (msgSendThreadLock)
            {
                // 强制唤醒所有等待线程
                Monitor.PulseAll(msgSendThreadLock);
            }*/
            Status = NetClientStatus.Close;
            AppLogger.Warning("连接关闭 Close Socket");
        }
        internal void Quit()
        {
            Close();
        }
        #endregion

        #region 功能函数
        /// <summary>
        /// 重新链接服务器
        /// </summary>
        public void ReConnectToServer()
        { 
            if (Status==NetClientStatus.ConnectFail
                ||Status==NetClientStatus.Disconnect
                ||Status==NetClientStatus.Close
                ||Status==NetClientStatus.Closing)
            { 
                Connect();
            }
        }
        #endregion
    }
}
