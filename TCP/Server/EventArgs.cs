using System;
using System.Diagnostics.CodeAnalysis;

namespace RSJWYFamework.Runtime
{ 
            /// <summary>
            /// TCP服务端事件
            /// </summary>
            public abstract class TCPServerSoketBaseEventArgs:EventArgsBase
            {
                
            }
            /// <summary>
            /// 客户端连接上来的事件
            /// </summary>
            public sealed class ServerClientConnectedCallBackEventArgs :TCPServerSoketBaseEventArgs
            {
                /// <summary>
                /// 发出的TCPServerHandle
                /// </summary>
                public Guid ServerHandle { get;private set; }
                /// <summary>
                /// 连接上来的客户端Handle
                /// </summary>
                public Guid ClientHandle { get;private set; }
                public ServerClientConnectedCallBackEventArgs(Guid serverHandle, Guid clientHandle)
                {
                    ServerHandle = serverHandle;
                    ClientHandle = clientHandle;
                }
            }
            /// <summary>
            /// 客户端离线的事件
            /// </summary>
            public sealed class ServerCloseClientCallBackEventArgs : TCPServerSoketBaseEventArgs
            {
                /// <summary>
                /// 发出的TCPServerHandle
                /// </summary>
                public Guid ServerHandle { get; private set; }
                /// <summary>
                /// 发生离线的客户端Handle
                /// </summary>
                public Guid ClientHandle { get;private set; }
                public ServerCloseClientCallBackEventArgs(Guid serverHandle, Guid clientHandle)
                {
                    ServerHandle = serverHandle;
                    ClientHandle = clientHandle;
                }
            }
            /// <summary>
            /// 服务端状态事件
            /// </summary>
            public sealed class ServerStatusEventArgs : TCPServerSoketBaseEventArgs
            {
                /// <summary>
                /// 服务端Handle
                /// </summary>
                public Guid ServerHandle { get; private set; }
                /// <summary>
                /// 服务端状态
                /// </summary>
                public NetServerStatus status{ get; private set; }
             
                public ServerStatusEventArgs(Guid serverHandle,NetServerStatus status)
                {
                    ServerHandle = serverHandle;
                    this.status = status;
                }
            }
            
            
            /// <summary>
            /// 收到客户端发来的消息
            /// </summary>
            public sealed class FromClientReceiveMsgEventArgs : TCPServerSoketBaseEventArgs
            {
                public FromTCPClientMsg MSGContainer { get;private set; }

                public FromClientReceiveMsgEventArgs(FromTCPClientMsg msgContainer)
                {
                    MSGContainer=msgContainer;
                }
            }
            
            /// <summary>
            /// 向客户端发送消息
            /// <remarks>
            /// 指定服务端客户端发送
            /// </remarks>
            /// </summary>
            public sealed class ServerToClientMsgEventArgs : TCPServerSoketBaseEventArgs
            {
                public SendToClientMsgContainer msgContainer { get; private set; }

                /// <summary>
                /// 指定服务端指定客户端发送消息
                /// </summary>
                /// <returns></returns>
                public static ServerToClientMsgEventArgs CreateSTC(byte[] data, Guid serverHandle, Guid clientHandle)
                {
                    var args = new ServerToClientMsgEventArgs();
                    args.msgContainer = SendToClientMsgContainer.CreateSTC(data, serverHandle, clientHandle);
                    return args;
                }
                /// <summary>
                /// 向所有服务端连上来的所有客户端发送消息
                /// </summary>
                /// <returns></returns>
                public static ServerToClientMsgEventArgs CreateASTAC(byte[] data)
                {
                    var args = new ServerToClientMsgEventArgs();
                    args.msgContainer = SendToClientMsgContainer.CreateASTAC(data);
                    return args;
                }
                /// <summary>
                /// 向指定服务端连上来的所有客户端发送消息
                /// </summary>
                /// <returns></returns>
                public static ServerToClientMsgEventArgs CreateSTAC(byte[] data, Guid clientHandle)
                {
                    var args = new ServerToClientMsgEventArgs();
                    args.msgContainer = SendToClientMsgContainer.CreateSTAC(data, clientHandle);
                    return args;
                }
            }
    
            /// <summary>
            /// 向客户端发送消息完成回调
            /// <remarks></remarks>
            /// </summary>
            public sealed class SendMsgToClientCallBackEventArgs : TCPServerSoketBaseEventArgs
            {
                public TCPServertToClientMsgCallBack CallBack { get; private set; }
                /// <summary>
                /// 构造函数
                /// </summary>
                internal SendMsgToClientCallBackEventArgs(TCPServertToClientMsgCallBack callBack)
                {
                    CallBack = callBack;
                }
            }
}