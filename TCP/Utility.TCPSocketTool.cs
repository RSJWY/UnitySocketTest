using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using UnityEngine;

namespace RSJWYFamework.Runtime
{
    public static partial class Utility
    {
        public static class TCPSocketTool
        {
            // 定义一个固定的字节数组作为心跳包
            public static readonly byte[] HeartbeatPacket = new byte[]
            {
                0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22,
                0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00
            };

            public static Guid GetPingGUID()
            {
                return new Guid(HeartbeatPacket);
            }

            /// <summary>
            /// 反序列化本次的消息
            /// </summary>
            /// <param name="byteArray">存储消息的自定义数组（已在分包粘包处理时，移除了长度位）</param>
            /// <param name="iSocketMsgBodyEncrypt">解密接口服务</param>
            /// <returns>封装的消息信息</returns>
            internal static (byte[] msgBase, bool IsPingPong) DecodeMsg(Memory<byte> byteArray, ISocketMsgBodyEncrypt iSocketMsgBodyEncrypt)
            {
                //确认数据是否有误
                if (byteArray.Length <= 0)
                {
                    throw new AppException($"发生异常，数据长度于0，无法对数据处理");
                }
                //消息体-计算协议体长度，
                //外部在处理分包粘包时，已经处理完了长度信息，本阶段移除CRC32信息，获得消息体
                int bodyCount = byteArray.Length - 4; 
                //映射处理
                Memory<byte> remoteCRC32Bytes = byteArray.Slice(bodyCount, 4); //取校验码
                Memory<byte> bodyBytes = byteArray.Slice(0, bodyCount); //取数据载体
                //校验CRC32
                uint localCRC32 = Utility.CRC32.GetCrc32(bodyBytes.ToArray()); //获取协议体的CRC32校验码
                uint remoteCRC32 = Utility.ByteArrayToUInt(remoteCRC32Bytes.ToArray()); // 运算获取远端计算的验码
                if (localCRC32 != remoteCRC32)
                {
                    throw new AppException($"CRC32校验失败！！远端CRC32：{remoteCRC32}，本机计算的CRC32：{localCRC32}。协议体的一致性遭到破坏");
                }
                //对比是不是心跳包
                if (!bodyBytes.Span.SequenceEqual(HeartbeatPacket))
                {
                    //解密消息
                    byte[] decryptBodyBytes=iSocketMsgBodyEncrypt==null?bodyBytes.ToArray():iSocketMsgBodyEncrypt.Decrypt(bodyBytes.ToArray());
                    return (decryptBodyBytes.ToArray(), false);
                }
                else
                {
                    //返回心跳包确认
                    return (null, true);
                }
            }

            /// <summary>
            /// 将消息进行序列化
            /// </summary>
            /// <param name="msgBase">要处理的消息体</param>
            /// <param name="iSocketMsgBodyEncrypt">加密接口</param>
            /// <returns>序列化后的消息</returns>
            internal static ByteArrayMemory EncodeMsg(byte[] msgBase, ISocketMsgBodyEncrypt iSocketMsgBodyEncrypt)
            {
                // 协议体编码
                //Memory<byte> nameBytes = EncodeName(msgBase); // 协议名编码
                //加密数据
                Memory<byte> bodyBytes=iSocketMsgBodyEncrypt==null?msgBase:iSocketMsgBodyEncrypt.Encrypt(msgBase);

                // 获取校验码
                uint crc32 = Utility.CRC32.GetCrc32(bodyBytes.ToArray()); // 获取协议体的CRC32校验码
                Memory<byte> bodyCrc32Bytes = UIntToByteArray(crc32); // 编码CRC32校验码为字节数组

                // 总长度计算
                uint totalLength = (uint)bodyBytes.Length + (uint)bodyCrc32Bytes.Length + sizeof(uint); // 加上头部长度的大小

                // 创建足够大小的数组来存储整个消息
                Memory<byte> sendBytes = new byte[totalLength];

                // 组装字节流数据
                // 编码记录长度（头部长度不包括头部本身的长度），并复制到memory开始位
                BitConverter.GetBytes(totalLength - sizeof(int)).AsSpan().CopyTo(sendBytes.Span);
                //复制数据到memory（从指定位置开始）
                bodyBytes.CopyTo(sendBytes.Slice(sizeof(int)));
                //复制CRC校验码到末尾
                bodyCrc32Bytes.CopyTo(sendBytes.Slice(sizeof(int) + bodyBytes.Length));

                // 将拼接好的信息用自定义的消息数组保存
                ByteArrayMemory ba = new ByteArrayMemory(sendBytes.ToArray());

                return ba;
            }

            /// <summary>
            /// 创建心跳包特征消息
            /// </summary>
            /// <remarks>内部直接构建</remarks>
            /// <returns></returns>
            internal static ByteArrayMemory SendPingPong()
            {
                // 创建一个与原数组长度相同的新数组
                byte[] newPacket = new byte[HeartbeatPacket.Length]; 
                // 复制内容
                Array.Copy(HeartbeatPacket, newPacket, HeartbeatPacket.Length);
                return EncodeMsg(HeartbeatPacket, null);
            }
        }
    }
}