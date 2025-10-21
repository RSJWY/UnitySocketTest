using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 针对于SocketAsyncEventArgs特殊的对象池
    /// </summary>
    public sealed class SocketAsyncEventPool: ObjectPool<SocketAsyncEventArgs>
    {
        public SocketAsyncEventPool(Action<SocketAsyncEventArgs> onCreate, Action<SocketAsyncEventArgs> onDestroy, 
            Action<SocketAsyncEventArgs> onGet, Action<SocketAsyncEventArgs> onRelease, int limit, int initCount) 
            : base(onCreate, onDestroy, onGet, onRelease, limit, initCount)
        {
            
        }

        /// <summary>
        /// 设置一个空的SocketAsyncEventArgs
        /// </summary>
        /// <returns></returns>
        public SocketAsyncEventArgs GetNullBuffer()
        {
            if (_objectQueue.TryPop(out var popitem))
            {
                _onGet?.Invoke(popitem);
                return popitem;
            }
            else
            {
                var item= new SocketAsyncEventArgs();
                _onCreate?.Invoke(item);
                item.SetBuffer(null,0,0);
                return item;
            }
        }

        
    }
}