using System;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 消息体加解密服务
    /// </summary>
    public interface ISocketMsgBodyEncrypt
    {
        /// <summary>
        /// 加密消息体
        /// </summary>
        /// <param name="data">待加密数据-内存切片</param>
        /// <returns>加密后的数据</returns>
        byte[] Encrypt(byte[] data);
        /// <summary>
        /// 解密消息体
        /// </summary>
        /// <param name="data">待解密数据-内存切片</param>
        /// <returns>解密后的数据</returns>
        byte[] Decrypt(byte[] data);
    }
}