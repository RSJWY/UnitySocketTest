using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 网络链接事件枚举
    /// </summary>
    public enum NetServerStatus
    {
        None ,
        /// <summary>
        /// 正在打开监听
        /// </summary>
        OpenListening,
        /// <summary>
        /// 正在监听
        /// </summary>
        Listen,
        /// <summary>
        /// 正在关闭
        /// </summary>
        CloseListening,
        /// <summary>
        /// 关闭
        /// </summary>
        Close,
        /// <summary>
        /// 发生错误，无法监听，参数设置问题
        /// </summary>
        Fail
    }
    public class TcpServerService
    {  

        private NetServerStatus _status;

        public NetServerStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    _tcpServerManager?.ServerServiceStatus(_handle,_status);
                }
            }
        }
        
        /// <summary>
        /// 消息体加密接口
        /// </summary>
        internal ISocketMsgBodyEncrypt  _MsgBodyEncrypt;
        /// <summary>
        /// 监听端口
        /// </summary>
        int _port = 5236;

        /// <summary>
        /// 监听IP
        /// </summary>
        IPAddress _ip = IPAddress.Any;

        /// <summary>
        /// 心跳包间隔时间
        /// </summary>
        internal long pingInterval = 60;

        /// <summary>
        /// 服务器监听Socket
        /// </summary>
        private Socket ListenSocket;


        /// <summary>
        /// 客户端容器字典
        /// </summary>
        internal ConcurrentDictionary<Guid, ClientSocketContainer> ClientDic { get; private set; }= new ();

        /// <summary>
        /// 服务器接收的消息队列
        /// </summary>
        private ConcurrentQueue<FromTCPClientMsg> serverMsgQueue = new();


        /// <summary>
        /// 消息处理线程
        /// </summary>
        private Thread msgThread;
        /// <summary>
        /// 心跳包监测线程
        /// </summary>
        private Thread pingThread;

        /// <summary>
        /// 收到消息处理线程-通知多线程自己跳出
        /// </summary>
        private CancellationTokenSource cts;
        

        /// <summary>
        /// SocketAsyncEventArgs池-读写
        /// </summary>
        private SocketAsyncEventPool _socketAsynEPRW;
        
        /// <summary>
        /// 绑定的服务端控制器
        /// </summary>
        private TcpServerManager _tcpServerManager;
        
        /// <summary>
        /// 服务句柄
        /// </summary>
        private Guid _handle;
        
        /// <summary>
        /// 是否开启调试心跳包
        /// </summary>
        private bool _isDebugPingPong = false;
        /// <summary>
        /// 数据缓冲区
        /// </summary>
        private int _bufferSize = 10485760;
        /// <summary>
        /// 对象池大小
        /// </summary>
        private int _limit = 100;
        /// <summary>
        /// 初始化对象数量
        /// </summary>
        private int _initCount = 10;

        #region 初始化

        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ip">监听IP，确保校验正确</param>
        /// <param name="port">监听端口</param>
        /// <param name="tcpServerManager">服务端控制器</param>
        /// <param name="handle">服务句柄</param>
        /// <param name="msgBodyEncrypt">消息体加密接口</param>
        /// <param name="isDebugPingPong">是否开启调试心跳包</param>
        /// <param name="bufferSize">数据缓冲区大小</param>
        /// <param name="limit">对象池大小</param>
        /// <param name="initCount">初始化对象数量</param>
        internal TcpServerService(string ip, int port,TcpServerManager tcpServerManager, 
            Guid handle,ISocketMsgBodyEncrypt msgBodyEncrypt,
            bool isDebugPingPong = false,int bufferSize = 10485760,int limit = 100,int initCount = 10)
        {
            _ip = IPAddress.Parse(ip);
            _port = port;
            _tcpServerManager = tcpServerManager;
            _handle = handle;
            _MsgBodyEncrypt = msgBodyEncrypt;
            _isDebugPingPong = isDebugPingPong;
            _bufferSize = bufferSize;
            _limit = limit;
            _initCount = initCount; 
        }
        

        /// <summary>
        /// 初始化监听
        /// </summary>
        internal void Bind()
        {
            AppLogger.Log($"服务端启动参数，IP：{_ip.ToString()}，port：{_port}");
            Status = NetServerStatus.OpenListening;
            //初始化池
            _socketAsynEPRW = new(
                (_obj) =>
                {
                    _obj.Completed += IO_Completed;
                },
                (_obj) =>
                {
                    _obj.Dispose();
                },
                (_obj) =>
                {
                    var _buffer = new byte[_bufferSize]; 
                    _obj.SetBuffer(_buffer,0,_buffer.Length);
                },
                (_obj) =>
                {
                    _obj.UserToken = null;
                    _obj.SetBuffer(null,0,0);
                },
                _limit,_initCount);
            //配置连接信息
            try
            {
                //监听IP
                IPAddress m_ip = _ip;
                //设置监听端口
                IPEndPoint ipendpoint = new IPEndPoint(m_ip, _port);
                //创建Socket并设置模式
                ListenSocket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //绑定监听Socket监听
                ListenSocket.Bind(ipendpoint);
                //链接数量限制
                ListenSocket.Listen(100);
                //启动线程
                cts = new ();
                //消息处理线程-后台处理
                msgThread = new Thread(() => FromClientMsgReceiveThread(cts.Token));
                msgThread.IsBackground = true;//后台运行
                msgThread.Start();
                StartAccept(null);
            }
            catch (Exception e)
            {
                Status = NetServerStatus.Fail;
                AppLogger.Error($" 服务端启动监听 IP:{_ip}，Port:{_port}失败！！错误信息：\n {e}");
                
            }
        }

        #endregion

        #region 功能处理
        
        /// <summary>
        /// 开启接受连接请求
        /// </summary>
        /// <param name="acceptEventArg">监听SocketAsyncEventArgs</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)  
        {  
            if (acceptEventArg == null)  
            {  
                //没有，创建一个新的
                acceptEventArg = new SocketAsyncEventArgs();  
                acceptEventArg.Completed += (sender, e) =>
                {
                    Task.Run(() => ProcessAccept(e));
                };  
            }  
            else  
            {  
                // socket 必须清除，因为 Context 对象正在被重用
                acceptEventArg.AcceptSocket = null;  
            }  
            try
            {
                //绑定时必须检查，绑定时已完成不会触发回调
                //https://learn.microsoft.com/zh-cn/dotnet/api/system.net.sockets.socket.acceptasync?view=netframework-4.8.1#system-net-sockets-socket-acceptasync(system-net-sockets-socketasynceventargs)
                if (!ListenSocket.AcceptAsync(acceptEventArg))
                    Task.Run(() => ProcessAccept(acceptEventArg));
                Status = NetServerStatus.Listen;
                //输出日志
                AppLogger.Log($"服务端开启监听：{ListenSocket.LocalEndPoint.ToString()}");
            }
            catch (Exception e)
            {
                Status = NetServerStatus.Fail;
                AppLogger.Error($" AcceptAsync监听时发生异常\n {e}");
            }
        } 
        /// <summary>
        /// 接受连接请求
        /// </summary>
        private void ProcessAccept(SocketAsyncEventArgs socketAsyncEArgs)  
        {
            // 接受下一个连接
            if (socketAsyncEArgs.SocketError == SocketError.OperationAborted)
            {
                AppLogger.Warning($"socket操作中止，不再接收新的连接请求；{socketAsyncEArgs.SocketError}");
                return;
            }
            try
            {

                //创建并存储客户端信息
                var clientToken = new ClientSocketContainer
                {
                    lastPingTime = Utility.Timestamp.UnixTimestampSeconds,
                    socket = socketAsyncEArgs.AcceptSocket,
                    ReadBuff = new(),
                    readSocketAsyncEA = _socketAsynEPRW.Get(),
                    writeSocketAsyncEA = _socketAsynEPRW.GetNullBuffer(),
                    ConnectTime = DateTime.Now,
                    Remote = socketAsyncEArgs.AcceptSocket.RemoteEndPoint,
                    IPAddress = ((IPEndPoint)(socketAsyncEArgs.AcceptSocket.RemoteEndPoint)).Address,
                    sendQueue = new(),
                    cts = new(),
                    msgSendDoneEvent =  new ManualResetEventSlim(false),
                    ServerService=this,
                    TokenID = Guid.NewGuid(),
                };
                //绑定线程
                //消息发送线程
                clientToken.msgSendThread = new Thread(
                    () => MsgSendListenThread(clientToken))
                {
                    IsBackground = true
                };
                clientToken.msgSendThread.Start();
                //心跳监控线程
                clientToken.PingPongThread = new Thread(
                    () => clientToken.PongThread())
                {
                    IsBackground = true
                };
                clientToken.PingPongThread.Start();

                //添加和绑定
                ClientDic.TryAdd(clientToken.TokenID, clientToken);
                clientToken.readSocketAsyncEA.UserToken = clientToken;
                clientToken.writeSocketAsyncEA.UserToken = clientToken;
                //广播消息
                _tcpServerManager.ClientConnectedCallBack(_handle,clientToken.TokenID);
                AppLogger.Log($"一个客户端连接上来：{clientToken.Remote},当前设备数：{ClientDic.Count}");

                //接受消息传入请求
                if (!clientToken.socket.ReceiveAsync(clientToken.readSocketAsyncEA))
                    Task.Run(() => ProcessReceive(clientToken.readSocketAsyncEA)); // 异步执行接收处理，避免递归调用
            }
            catch (Exception e)
            {
                AppLogger.Error( $" ReceiveAsync接受连接时发生异常\n {e}");
            }
  
            StartAccept(socketAsyncEArgs);  
        }
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
                    AppLogger.Warning($"TCP IO_Completed 在套接字上完成的最后一个操作不是接收或发送，{e.LastOperation}");  
                    break;
            }  
        }  
        
        /// <summary>
        /// 处理数据传入
        /// </summary>
        private void ProcessReceive(SocketAsyncEventArgs socketAsyncEA)
        {
            // 检查连接状态
            var clientToken = (ClientSocketContainer)socketAsyncEA.UserToken;
            if (!ClientDic.ContainsKey(clientToken.TokenID))
            {
                //可能后期会移除
                AppLogger.Warning($"无法找到用于存储的客户端容器，无法执行接收,可能已经断开链接，当前数量为：{ClientDic.Count}");
                return;//找不到存储的容器
            }
            try
            {
                var readBuff = clientToken.ReadBuff;//获取客户端容器的字节容器
                //必须有可读数据，否则将关闭连接
                if (socketAsyncEA.BytesTransferred > 0 && socketAsyncEA.SocketError == SocketError.Success)
                {
                    //首先判断客户端token缓冲区剩余空间是否支持数据拷贝
                    if (readBuff.Remain<=socketAsyncEA.BytesTransferred)
                        readBuff.ReSize(readBuff.WriteIndex+socketAsyncEA.BytesTransferred);//确保从写入索引开始，能写入数据
                    //拷贝到容器缓冲区
                    lock (clientToken.ReadBuff)
                    {
                        //从缓冲区获取并设置数据
                        readBuff.SetBytes(socketAsyncEA.Buffer,socketAsyncEA.Offset,socketAsyncEA.BytesTransferred);
                        socketAsyncEA.SetBuffer(0, socketAsyncEA.Buffer.Length);
                        //处理本次接收的数据
                        //使用循环，直至没有可读的完整数据
                        while (readBuff.Readable>4)
                        {
                            //获取消息长度
                            int msgLength = BitConverter.ToInt32(readBuff.GetlengthBytes(4).ToArray());
                            //判断是不是分包数据
                            if (readBuff.Readable < msgLength + 4)
                            {
                                //如果消息长度小于读出来的消息长度
                                //此为分包，不包含完整数据
                                //因为产生了分包，可能容量不足，根据目标大小进行扩容到接收完整
                                //扩容后，retun，继续接收
                                readBuff.MoveBytes(); //已经完成一轮解析，移动数据
                                readBuff.ReSize(msgLength + 8); //扩容，扩容的同时，保证长度信息也能被存入
                                
                                // 注意要再次绑定监听再离开以继续接收
                                if (!clientToken.socket.ReceiveAsync(socketAsyncEA))
                                    Task.Run(() => ProcessReceive(socketAsyncEA));
                                return;
                            }
                            //消息完整，处理数据
                            //移动，规避长度位，从整体消息开始位接收数据
                            readBuff.ReadIndex += 4; //前四位存储字节流数组长度信息
                            //在消息接收异步线程内同步处理消息，保证当前客户消息顺序性
                            //var _msgBase= MessageTool.DecodeMsg(readBuff.Bytes, readBuff.ReadIndex, msgLength);
                            var decodeMsg= Utility.TCPSocketTool.DecodeMsg(readBuff.GetlengthBytes(msgLength),_MsgBodyEncrypt);
                            
                            //刷新心跳时间
                            clientToken.lastPingTime = Utility.Timestamp.UnixTimestampSeconds;
                            //检查是否是心跳包
                            if (decodeMsg.IsPingPong)
                            {
                                
                                //生成心跳包返回给客户端
                                InternalSendToClientMsgContainer pingpongMsg = new()
                                {
                                    TargetContainer = clientToken,
                                    SendBytes = Utility.TCPSocketTool.SendPingPong(),
                                    SendType = SendToClientMsgType.STC,
                                };
                                clientToken.sendQueue.Enqueue(pingpongMsg);
                                if (_isDebugPingPong)
                                    AppLogger.Log($"<color=blue>接收到客户端{clientToken.Remote}心跳包，返回心跳包</color>");
                            }
                            else
                            {
                                //把数据放入队列
                                var msgContainer = new FromTCPClientMsg()
                                {
                                    TCPServerHandle = _handle,
                                    TCPClientHandle = clientToken.TokenID,
                                    msgBytes= decodeMsg.msgBase,
                                    Success = true,
                                    Error = string.Empty,
                                }; 
                                serverMsgQueue.Enqueue(msgContainer);
                            }
                            //处理完后移动数据位
                            readBuff.ReadIndex += msgLength;
                            //检查是否需要扩容
                            readBuff.CheckAndMoveBytes();
                            //结束本次处理循环，如果粘包，下一个循环将会处理
                        }
                    }
                    //继续接收. 为什么要这么写,请看Socket.ReceiveAsync方法的说明  
                    if (!clientToken.socket.ReceiveAsync(socketAsyncEA))
                        Task.Run(() => ProcessReceive(socketAsyncEA));
                }
                else
                {
                    AppLogger.Warning($"读取客户端：{clientToken.TokenID}-{clientToken.Remote}发来的消息出错！！错误信息： {socketAsyncEA.SocketError}、{socketAsyncEA.BytesTransferred}，将关闭本链接" );
                    CloseClientSocket(clientToken);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"读取客户端：{clientToken.TokenID}-{clientToken.Remote}发来的消息出错！！错误信息： {ex.ToString()}，将关闭本链接" );
                CloseClientSocket(clientToken);
            }
        }

        /// <summary>
        /// 发送信息到所有客户端
        /// </summary>
        internal void SendToAllClientMessage(SendToClientMsgContainer msgContainer)
        {
            foreach (var client in ClientDic)
            {
                SendMessage(msgContainer.data,client.Value,msgContainer.MsgToken,msgContainer.SendType);
            }
        }
        
        /// <summary>
        /// 发送信息到客户端
        /// </summary>
        internal void SendToClientMessage(SendToClientMsgContainer msgContainer)
        {
            if (ClientDic.TryGetValue(msgContainer.ClientHandle,out var targetContainer))
            {
                SendMessage(msgContainer.data,targetContainer,msgContainer.MsgToken,msgContainer.SendType);
            }
            else
            {
                AppLogger.Warning($"未找到目标客户端，无法发送消息");
            }
        }

        /// <summary>
        /// 发送信息到客户端内部操作
        /// </summary>
        /// <param name="msgBytes"></param>
        /// <param name="targetContainer"></param>
        /// <param name="MsgToken"></param>
        void SendMessage(byte[] msgBytes,ClientSocketContainer targetContainer,Guid MsgToken,SendToClientMsgType msgType)
        {
            if (targetContainer?.socket is not { Connected: true })
            {
                AppLogger.Error($"Socket链接未设置或者未建立链接");
                return;//链接不存在或者未建立链接
            }
            //写入数据
            try
            {
                if (!targetContainer.socket.Poll(100, SelectMode.SelectWrite))
                {
                    AppLogger.Error($"Socket状态不可写");
                    return;//链接不存在或者未建立链接
                }
                //编码
                var sendBytes = Utility.TCPSocketTool.EncodeMsg(msgBytes,_MsgBodyEncrypt);
                //创建容器
                InternalSendToClientMsgContainer msg = new()
                {
                    TargetContainer = targetContainer,
                    SendBytes = sendBytes,
                    MsgToken = MsgToken,
                    SendType = msgType,
                };
                targetContainer.sendQueue.Enqueue(msg);
                //写入到队列，向客户端发送消息，根据客户端和绑定的数据发送
            }
            catch (SocketException ex)
            {
                AppLogger.Error($"向客户端发送消息失败 SendMessage Error:{ex.ToString()}");
                //CloseSocket(ClientDic[_client]);?断开方式有待商讨
            }
        }
        
        
        /// <summary>
        /// 数据发送后回调
        /// </summary>
        /// <param name="socketAsyncEA"></param>
        private void ProcessSend(SocketAsyncEventArgs socketAsyncEA)
        {
            var clientToken = (ClientSocketContainer)socketAsyncEA.UserToken;
            if (!ClientDic.ContainsKey(clientToken.TokenID))
            {
                //可能后期会移除
                AppLogger.Warning($"无法找到用于存储的客户端容器，无法执行接收,可能已经断开链接，当前数量为：{ClientDic.Count}");
                //return;//找不到存储的容器
            }

            try
            {
                if (socketAsyncEA.SocketError == SocketError.Success && socketAsyncEA.BytesTransferred > 0)
                {
                    //获取本次数据操作长度，对比应该发送长度
                    int senlength = socketAsyncEA.BytesTransferred;
                    //取出消息类但不移除，用作数据比较，根据消息MSG队列，获取相应的客户端内的消息数组队列
                    clientToken.sendQueue.TryPeek(out var _msgbase);
                    var _ba = _msgbase.SendBytes;
                    _ba.ReadIndex += senlength; //已发送索引
                    if (_ba.Readable == 0) //代表发送完整
                    {
                        clientToken.sendQueue.TryDequeue(out var _); //取出但不使用，只为了从队列中移除
                        _ba = null; //发送完成
                        //发送完成，调用回调
                        if (_msgbase.MsgToken!=Guid.Empty)
                        {
                            var _callBack = new TCPServertToClientMsgCallBack()
                            {
                                CallBackType = TCPServertToClientMsgCallBack.SendCallBackType.Success,
                                SocketError = socketAsyncEA.SocketError,
                                Error = string.Empty,
                                MsgToken = _msgbase.MsgToken,
                                TCPServerHandle = _handle,
                                TCPClientHandle = clientToken.TokenID,
                            };
                            _tcpServerManager.SendMsgToClientCallBack(_callBack);
                        }
                    }
                    else
                    {
                        //发送不完整，再次发送
                        //重新获取可用数据切片作为发送数据
                        socketAsyncEA.SetBuffer(_ba.GetRemainingSlices());
                        if (!_msgbase.TargetContainer.socket.SendAsync(socketAsyncEA))
                            Task.Run(() => ProcessSend(socketAsyncEA));
                    }
                }
                else
                {
                    AppLogger.Warning($"向服务器发送消息失败 ProcessSend Error:不满足回调进入条件；{socketAsyncEA.SocketError}");
                    CloseClientSocket(clientToken);
                    //发送完成，调用回调
                    var _callBack = new TCPServertToClientMsgCallBack()
                    {
                        CallBackType = TCPServertToClientMsgCallBack.SendCallBackType.Error,
                        SocketError = socketAsyncEA.SocketError,
                        Error = string.Empty,
                        MsgToken = Guid.Empty,
                        TCPServerHandle = _handle,
                        TCPClientHandle = clientToken.TokenID,
                    };
                    _tcpServerManager.SendMsgToClientCallBack(_callBack);
                }

            }
            catch (Exception exception)
            {
                CloseClientSocket(clientToken);
                AppLogger.Error($"向客户端发送消息失败 SendCallBack Error:{exception}");
                //发送完成，调用回调
                var _callBack = new TCPServertToClientMsgCallBack()
                {
                    CallBackType = TCPServertToClientMsgCallBack.SendCallBackType.Exception,
                    SocketError = socketAsyncEA.SocketError,
                    Error = exception.Message,
                    MsgToken = Guid.Empty,
                    TCPServerHandle = _handle,
                    TCPClientHandle = clientToken.TokenID,
                };
                _tcpServerManager.SendMsgToClientCallBack(_callBack);
            }
            finally
            {
                //无论状态如何，释放线程
                clientToken.msgSendDoneEvent.Set();
            }
        }  
        
        
        
        /// <summary>
        /// 关闭客户端
        /// </summary>
        /// <param name="targetSocket"></param>
        internal void CloseClientSocket(ClientSocketContainer clientContainer)
        {
            ClientDic.TryRemove(clientContainer.TokenID, out var _);
            _tcpServerManager.CloseClientReCallBack(_handle,clientContainer.TokenID);
            clientContainer.Close();
            // 释放SocketAsyncEventArgs,并放回池子中
            if (clientContainer.readSocketAsyncEA!=null)
                _socketAsynEPRW.Release(clientContainer.readSocketAsyncEA);
            if (clientContainer.writeSocketAsyncEA != null)
                _socketAsynEPRW.Release(clientContainer.writeSocketAsyncEA);
            AppLogger.Log($"一个客户端断开连接，当前连接总数：{ClientDic.Count}");
        }
        /// <summary>
        /// 关闭服务器，关闭所有已经链接上来的socket以及关闭多余线程
        /// </summary>
        public void CloseServer()   
        {
            Status = NetServerStatus.CloseListening;
            cts.Cancel();
            //关闭所有已经链接上来的socket
            List<ClientSocketContainer> tmp = ClientDic
                .Select(x=>x.Value)
                .ToList();
            foreach (var t in tmp)
            {
                CloseClientSocket(t);
            }
            ListenSocket.Close();
            AppLogger.Log($"已关闭所有链接上来的客户端");
            Status = NetServerStatus.Close;
        }


        #endregion
        
        #region 线程
        /// <summary>
        /// 消息队列发送监控线程
        /// <remarks>
        /// 每个客户端容器内都有一个发送监控线程，用于监控发送队列是否有数据
        /// 如果有数据，就会从队列中取出数据，使用其绑定的客户端socket发送消息
        /// </remarks>
        /// </summary>
        void MsgSendListenThread(ClientSocketContainer clientSocketContainer)
        {
            while (!clientSocketContainer.cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (clientSocketContainer.sendQueue.Count <= 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    //取出消息队列内的消息，但不移除队列，以获取目标客户端
                    clientSocketContainer.sendQueue.TryPeek(out var _msgbase);
                    //_msgbase.targetToken.writeSocketAsyncEA.SetBuffer(_msgbase.SendOldBytes.Bytes, _msgbase.SendOldBytes.ReadIndex,_msgbase.SendOldBytes.length);
                    var data = _msgbase.SendBytes.GetRemainingSlices();
                    _msgbase.TargetContainer.writeSocketAsyncEA.SetBuffer(data);
                    //当前线程执行休眠，等待消息发送完成后继续
                    clientSocketContainer.msgSendDoneEvent.Reset();
                    if (!_msgbase.TargetContainer.socket.SendAsync( _msgbase.TargetContainer.writeSocketAsyncEA))
                        Task.Run(()=>ProcessSend(_msgbase.TargetContainer.writeSocketAsyncEA));
                    //等待SendCallBack完成回调释放本锁再继续执行
                    clientSocketContainer.msgSendDoneEvent.Wait(TimeSpan.FromSeconds(60));
                }
                catch (Exception ex)
                {
                    AppLogger.Error( $"消息发送时发生错误：{ex.Message}");
                    if (clientSocketContainer.cts.Token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// 消息处理线程，分发消息
        /// <remarks>
        /// 处理客户端发过来已经处理好的消息，
        /// 然后调用Manager的FromClientReceiveMsgCallBack方法
        /// </remarks>
        /// </summary>
        private void FromClientMsgReceiveThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (serverMsgQueue.Count <= 0)
                    {
                        Thread.Sleep(10);
                        continue;//当前无消息，跳过进行下一次排查处理
                    }
                    //取出并移除取出来的数据
                    if (!serverMsgQueue.TryDequeue(out var _msg))
                    {
                        AppLogger.Error($"取出并处理消息队列失败！！");
                    }
                    else
                    {
                        //通过Manager广播消息
                        if (_tcpServerManager == null)
                            throw new AppException( $"没有绑定控制器，无法发送客户端来的消息");
                        _tcpServerManager.FromClientReceiveMsgCallBack(_msg);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error( $"消息分发时发生错误：{ex.Message}");
                    if (token.IsCancellationRequested)
                    {
                        AppLogger.Warning( $"请求取消任务");
                        break;
                    }
                }
               
            }
        }

        #endregion

    }
}