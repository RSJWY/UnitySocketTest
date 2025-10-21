using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RSJWYFamework.Runtime
{
    public static partial class Utility
    {
        public static class SocketTool
        {

            /// <summary>
            /// 判断字符串是否为16进制
            /// </summary>
            /// <param name="input"></param>
            /// <returns></returns>
            public static bool IsHex(string input)
            {
                // 正则表达式匹配16进制值  
                string hexPattern = @"^[0-9A-Fa-f]+$";
                return Regex.IsMatch(input, hexPattern);
            }
            /*/// <summary>
            /// 时间误差检测，仅用作提示
            /// </summary>
            /// <param name="ServetOrClinet">服务器还是客户端，用作打印输出区分</param>
            /// <param name="NowTimeStamp">当前的时间戳</param>
            /// <param name="msgPing">心跳包类</param>
            public static void TimeDifference(string ServetOrClinet, long LocalTimeStamp, MsgBase msgPing)
            {
                MsgPing _msgPing = msgPing as MsgPing;
                long RemoteTimeStamp = _msgPing.timeStamp;//获取远端传来的时间戳
                if (RemoteTimeStamp <= 0)
                {
                    RSJWYLogger.Error("传入的值小于等于0，处理无效");
                    return;
                }
                //根据时间戳计算时间
                DateTime startTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime RemoteTime = startTime.AddSeconds(RemoteTimeStamp);//远端时间戳
                DateTime LocalTime = startTime.AddSeconds(LocalTimeStamp);//当前时间戳

                //计算误差
                long pingPongTimeStamp = LocalTimeStamp - RemoteTimeStamp;

                Debug.LogFormat($"{ServetOrClinet}消息：\n本地时间：{LocalTime}，远端时间：{RemoteTime}，时间戳时间差为{pingPongTimeStamp}");
                //Debug.LogFormat("{0}消息：\n本地时间：{3}远端时间为：{1}，\n和远端时间误差为：{2}s，请注意时间误差！！", ServetOrClinet, Remotetime, pingPongTime, TimeZoneInfo.ConvertTime(DateTime.Now, timeZoneInfo));
            }*/
            //版权声明：本文为CSDN博主「牛奶咖啡13」的原创文章，遵循CC 4.0 BY-SA版权协议，转载请附上原文出处链接及本声明。
            //原文链接：https://blog.csdn.net/xiaochenXIHUA/article/details/106199209

            /// <summary>
            /// 匹配IP地址是否合法
            /// </summary>
            /// <param name="ip">当前需要匹配的IP地址</param>
            /// <returns>true:表示合法</returns>
            public static bool MatchIP(string ip)
            {
                bool success = false;
                if (!string.IsNullOrEmpty(ip))
                {
                    //判断是否为IP
                    success = Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$");
                }

                return success;
            }

            /// <summary>
            /// 匹配端口是否合法
            /// </summary>
            /// <param name="port"></param>
            /// <returns>true：表示合法</returns>
            public static bool MatchPort(int port)
            {
                bool success = port is >= 1000 and <= 65535;
                return success;
            }


            /// <summary>
            /// 检查IP是否可ping通
            /// </summary>
            /// <param name="strIP">要检查的IP</param>
            /// <returns>是否可连通【true:表示可以连通】</returns>
            public static bool CheckIPIsPing(string strIP)
            {
                if (!string.IsNullOrEmpty(strIP))
                {
                    if (!MatchIP(strIP))
                    {
                        return false;
                    }

                    // Windows L2TP VPN和非Windows VPN使用ping VPN服务端的方式获取是否可以连通
                    Ping pingSender = new Ping();
                    PingOptions options = new PingOptions();

                    // 使用默认的128位值
                    options.DontFragment = true;

                    //创建一个32字节的缓存数据发送进行ping
                    string data = "testtesttesttesttesttesttesttest";
                    byte[] buffer = Encoding.ASCII.GetBytes(data);
                    int timeout = 120;
                    PingReply reply = pingSender.Send(strIP, timeout, buffer, options);

                    return (reply.Status == IPStatus.Success);
                }
                else
                {
                    return false;
                }
            }

            /// <summary>
            /// 连续几次查看是否某个IP可以PING通
            /// </summary>
            /// <param name="strIP">ping的IP地址</param>
            /// <param name="waitMilliSecond">每次间隔时间，单位：毫秒</param>
            /// <param name="testNumber">测试次数</param>
            /// <returns>是否可以连通【true:表示可以连通】</returns>
            public static bool MutiCheckIPIsPing(string strIP, int waitMilliSecond, int testNumber)
            {
                for (int i = 0; i < testNumber - 1; i++)
                {
                    if (CheckIPIsPing(strIP))
                    {
                        return true;
                    }

                    Thread.Sleep(waitMilliSecond);
                }

                return CheckIPIsPing(strIP);
            }


        }
    }
}
