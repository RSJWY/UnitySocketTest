using System;

namespace RSJWYFamework.Runtime
{
    /// <summary>
    /// 数据数组
    /// 数据数组结构：记录本条消息后续长度的数据（4位）+数据载体+CRC32校验码（4位长度）
    /// </summary>
    public class ByteArrayMemory
    {
        /// <summary>
        /// 默认大小
        /// </summary>
        public const int default_Size = 1024;
        /// <summary>
        /// 初始大小
        /// </summary>
        private int m_InitSize = 0;
        /// <summary>
        /// 缓冲区，存储数据的位置
        /// </summary>
        public Memory<byte> Bytes;
        /// <summary>
        /// 原始数组，防止被垃圾回收
        /// </summary>
        private byte[] _dataBuffer;

        
        /// <summary>
        /// 开始读索引
        /// </summary>
        public int ReadIndex = 0;//开始读索引
        /// <summary>
        /// 已经写入的索引
        /// </summary>
        public int WriteIndex = 0;//已经写入的索引
        /// <summary>
        /// 容量
        /// </summary>
        private int Capacity = 0;

        /// <summary>
        /// 剩余空间
        /// </summary>
        public int Remain { get { return Capacity - WriteIndex; } }

        /// <summary>
        /// 允许读取的数据长度
        /// </summary>
        public int Readable { get { return WriteIndex - ReadIndex; } }

        /// <summary>
        /// 长度
        /// </summary>
        public int Length => Bytes.Length;
        
        public ByteArrayMemory()
        {
            _dataBuffer = new byte[default_Size];
            Bytes = new Memory<byte>(_dataBuffer);
            Capacity = default_Size;
            m_InitSize = default_Size;
            ReadIndex = 0;
            WriteIndex = 0;
        }
        /// <summary>
        /// 发送信息构造函数
        /// </summary>
        public ByteArrayMemory(byte[] defaultBytes)
        {
            _dataBuffer = defaultBytes;
            Bytes = new Memory<byte>(defaultBytes);
            Capacity = defaultBytes.Length;
            m_InitSize = defaultBytes.Length;
            ReadIndex = 0;
            WriteIndex = defaultBytes.Length;
        }
        /*
        /// <summary>
        /// 发送信息构造函数
        /// </summary>
        public ByteArrayMemory(Memory<byte> defaultBytes)
        {
            
            Bytes = defaultBytes;
            Capacity = defaultBytes.Length;
            m_InitSize = defaultBytes.Length;
            ReadIndex = 0;
            WriteIndex = defaultBytes.Length;
        }*/
        /// <summary>
        /// 检查是否需要扩容
        /// </summary>
        public void CheckAndMoveBytes()
        {
            //长度8是保证能存储下整条消息长度信息的最小可用大小
            if (Remain < 8)
            {
                //可读空间不足
                //完成解析或者这次接收的数据是分包数据
                //剩余空间不足（容量-已经写入索引
                MoveBytes();
                ReSize(Readable * 2);
            }
        }
        /// <summary>
        /// 移动数据
        /// </summary>
        public void MoveBytes()
        {
            if (ReadIndex < 0)
            {
                return;
            }
            //拷贝数据，将已使用后的数据进行清除
            //Array.Copy(Bytes, ReadIndex, Bytes, 0, length);
            Bytes.Slice(ReadIndex, Readable).CopyTo(Bytes);
            //写入长度等于总长度
            WriteIndex = Readable;
            ReadIndex = 0;
        }
        /// <summary>
        /// 重设尺寸
        /// </summary>
        /// <param name="size">想要的存储空间</param>
        public void ReSize(int size)
        {
            if (ReadIndex < 0 || size < Readable || size < m_InitSize)
            {
                return;//开始读取位置小于0(超范围），可读数据长度大于要设置的新数据长度，初始空间大于要设置的新长度
            }
            int n = 1024;
            while (n < size)
            {
                //一直翻倍，直到可以容纳下要重设的数据长度
                n *= 2;
            }
            //重新指定容量大小
            Capacity = n;
            //创建新存储空间，使用新的长度
            _dataBuffer=new byte[Capacity];
            //拷贝现有数据到新空间
            Bytes.Slice(ReadIndex, Readable).CopyTo(_dataBuffer);
            Bytes = _dataBuffer;
            //重新读
            WriteIndex = Readable;
            ReadIndex = 0;
        }
        /// <summary>
        /// 获取剩余可用切片
        /// ReadIndex-Readable
        /// </summary>
        /// <returns></returns>
        public Memory<byte> GetRemainingSlices()
        {
            return Bytes.Slice(ReadIndex, Readable);
        }

        /// <summary>
        /// 设置数据到Memory
        /// </summary>
        /// <param name="buffer">数组</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">读取长度</param>
        public void SetBytes(byte[] buffer,int offset,int length)
        {
            // 检查是否需要扩容
            CheckAndMoveBytes();
            // 检查是否需要扩容
            if (Remain < length)
            {
                //翻倍扩容
                ReSize(length * 2);
            }
            // 获取目标 Memory<byte> 的一个切片（Slice），从 WriteIndex 开始，长度为 BytesTransferred
            Memory<byte> target = Bytes.Slice(WriteIndex, length);
            // 从 buffer 获取数据源的 Span<byte>
            Span<byte> source = new Span<byte>(buffer, offset, length);
            // 使用 Span.CopyTo 方法将数据从 source 复制到 target
            source.CopyTo(target.Span);
            // 更新 WriteIndex 以反映添加的数据量
            WriteIndex += length;
        }

        /// <summary>
        /// 获取指定长度的字节数组
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public Memory<byte> GetlengthBytes(int length)
        {
            return Bytes.Slice(ReadIndex, length);
        }

        public void Release()
        {
            var initialBytes = new byte[default_Size];
            Bytes = new Memory<byte>(initialBytes);
            Capacity = default_Size;
            m_InitSize = default_Size;
            ReadIndex = 0;
            WriteIndex = 0;
        }
    }
}

/*一次接收数据，ReadIndex在0位，WriteIndex移动到整个数据长度的位，确认是不是一个完整消息
 * 是，进行读取，不是WriteIndex不变，跳过，等待下一次数据。
 * 
 * 解析是否完整，每处里一条消息，ReadIndex进一步
 * WriteIndex-ReadIndex得结果就是这次读的长度length
 * 
 * 
 */
 /***                                                                          
 *          .,:,,,                                        .::,,,::.          
 *        .::::,,;;,                                  .,;;:,,....:i:         
 *        :i,.::::,;i:.      ....,,:::::::::,....   .;i:,.  ......;i.        
 *        :;..:::;::::i;,,:::;:,,,,,,,,,,..,.,,:::iri:. .,:irsr:,.;i.        
 *        ;;..,::::;;;;ri,,,.                    ..,,:;s1s1ssrr;,.;r,        
 *        :;. ,::;ii;:,     . ...................     .;iirri;;;,,;i,        
 *        ,i. .;ri:.   ... ............................  .,,:;:,,,;i:        
 *        :s,.;r:... ....................................... .::;::s;        
 *        ,1r::. .............,,,.,,:,,........................,;iir;        
 *        ,s;...........     ..::.,;:,,.          ...............,;1s        
 *       :i,..,.              .,:,,::,.          .......... .......;1,       
 *      ir,....:rrssr;:,       ,,.,::.     .r5S9989398G95hr;. ....,.:s,      
 *     ;r,..,s9855513XHAG3i   .,,,,,,,.  ,S931,.,,.;s;s&BHHA8s.,..,..:r:     
 *    :r;..rGGh,  :SAG;;G@BS:.,,,,,,,,,.r83:      hHH1sXMBHHHM3..,,,,.ir.    
 *   ,si,.1GS,   sBMAAX&MBMB5,,,,,,:,,.:&8       3@HXHBMBHBBH#X,.,,,,,,rr    
 *   ;1:,,SH:   .A@&&B#&8H#BS,,,,,,,,,.,5XS,     3@MHABM&59M#As..,,,,:,is,   
 *  .rr,,,;9&1   hBHHBB&8AMGr,,,,,,,,,,,:h&&9s;   r9&BMHBHMB9:  . .,,,,;ri.  
 *  :1:....:5&XSi;r8BMBHHA9r:,......,,,,:ii19GG88899XHHH&GSr.      ...,:rs.  
 *  ;s.     .:sS8G8GG889hi.        ....,,:;:,.:irssrriii:,.        ...,,i1,  
 *  ;1,         ..,....,,isssi;,        .,,.                      ....,.i1,  
 *  ;h:               i9HHBMBBHAX9:         .                     ...,,,rs,  
 *  ,1i..            :A#MBBBBMHB##s                             ....,,,;si.  
 *  .r1,..        ,..;3BMBBBHBB#Bh.     ..                    ....,,,,,i1;   
 *   :h;..       .,..;,1XBMMMMBXs,.,, .. :: ,.               ....,,,,,,ss.   
 *    ih: ..    .;;;, ;;:s58A3i,..    ,. ,.:,,.             ...,,,,,:,s1,    
 *    .s1,....   .,;sh,  ,iSAXs;.    ,.  ,,.i85            ...,,,,,,:i1;     
 *     .rh: ...     rXG9XBBM#M#MHAX3hss13&&HHXr         .....,,,,,,,ih;      
 *      .s5: .....    i598X&&A&AAAAAA&XG851r:       ........,,,,:,,sh;       
 *      . ihr, ...  .         ..                    ........,,,,,;11:.       
 *         ,s1i. ...  ..,,,..,,,.,,.,,.,..       ........,,.,,.;s5i.         
 *          .:s1r,......................       ..............;shs,           
 *          . .:shr:.  ....                 ..............,ishs.             
 *              .,issr;,... ...........................,is1s;.               
 *                 .,is1si;:,....................,:;ir1sr;,                  
 *                    ..:isssssrrii;::::::;;iirsssssr;:..                    
 *                         .,::iiirsssssssssrri;;:.                      
 */						 


/***
 *               ii.                                         ;9ABH,          
 *              SA391,                                    .r9GG35&G          
 *              &#ii13Gh;                               i3X31i;:,rB1         
 *              iMs,:,i5895,                         .5G91:,:;:s1:8A         
 *               33::::,,;5G5,                     ,58Si,,:::,sHX;iH1        
 *                Sr.,:;rs13BBX35hh11511h5Shhh5S3GAXS:.,,::,,1AG3i,GG        
 *                .G51S511sr;;iiiishS8G89Shsrrsh59S;.,,,,,..5A85Si,h8        
 *               :SB9s:,............................,,,.,,,SASh53h,1G.       
 *            .r18S;..,,,,,,,,,,,,,,,,,,,,,,,,,,,,,....,,.1H315199,rX,       
 *          ;S89s,..,,,,,,,,,,,,,,,,,,,,,,,....,,.......,,,;r1ShS8,;Xi       
 *        i55s:.........,,,,,,,,,,,,,,,,.,,,......,.....,,....r9&5.:X1       
 *       59;.....,.     .,,,,,,,,,,,...        .............,..:1;.:&s       
 *      s8,..;53S5S3s.   .,,,,,,,.,..      i15S5h1:.........,,,..,,:99       
 *      93.:39s:rSGB@A;  ..,,,,.....    .SG3hhh9G&BGi..,,,,,,,,,,,,.,83      
 *      G5.G8  9#@@@@@X. .,,,,,,.....  iA9,.S&B###@@Mr...,,,,,,,,..,.;Xh     
 *      Gs.X8 S@@@@@@@B:..,,,,,,,,,,. rA1 ,A@@@@@@@@@H:........,,,,,,.iX:    
 *     ;9. ,8A#@@@@@@#5,.,,,,,,,,,... 9A. 8@@@@@@@@@@M;    ....,,,,,,,,S8    
 *     X3    iS8XAHH8s.,,,,,,,,,,...,..58hH@@@@@@@@@Hs       ...,,,,,,,:Gs   
 *    r8,        ,,,...,,,,,,,,,,.....  ,h8XABMMHX3r.          .,,,,,,,.rX:  
 *   :9, .    .:,..,:;;;::,.,,,,,..          .,,.               ..,,,,,,.59  
 *  .Si      ,:.i8HBMMMMMB&5,....                    .            .,,,,,.sMr
 *  SS       :: h@@@@@@@@@@#; .                     ...  .         ..,,,,iM5
 *  91  .    ;:.,1&@@@@@@MXs.                            .          .,,:,:&S
 *  hS ....  .:;,,,i3MMS1;..,..... .  .     ...                     ..,:,.99
 *  ,8; ..... .,:,..,8Ms:;,,,...                                     .,::.83
 *   s&: ....  .sS553B@@HX3s;,.    .,;13h.                            .:::&1
 *    SXr  .  ...;s3G99XA&X88Shss11155hi.                             ,;:h&,
 *     iH8:  . ..   ,;iiii;,::,,,,,.                                 .;irHA  
 *      ,8X5;   .     .......                                       ,;iihS8Gi
 *         1831,                                                 .,;irrrrrs&@
 *           ;5A8r.                                            .:;iiiiirrss1H
 *             :X@H3s.......                                .,:;iii;iiiiirsrh
 *              r#h:;,...,,.. .,,:;;;;;:::,...              .:;;;;;;iiiirrss1
 *             ,M8 ..,....,.....,,::::::,,...         .     .,;;;iiiiiirss11h
 *             8B;.,,,,,,,.,.....          .           ..   .:;;;;iirrsss111h
 *            i@5,:::,,,,,,,,.... .                   . .:::;;;;;irrrss111111
 *            9Bi,:,,,,......                        ..r91;;;;;iirrsss1ss1111
 */

/***
 * ░░░░░░░░░░░░░░░░░░░░░░░░▄░░
 * ░░░░░░░░░▐█░░░░░░░░░░░▄▀▒▌░
 * ░░░░░░░░▐▀▒█░░░░░░░░▄▀▒▒▒▐
 * ░░░░░░░▐▄▀▒▒▀▀▀▀▄▄▄▀▒▒▒▒▒▐
 * ░░░░░▄▄▀▒░▒▒▒▒▒▒▒▒▒█▒▒▄█▒▐
 * ░░░▄▀▒▒▒░░░▒▒▒░░░▒▒▒▀██▀▒▌
 * ░░▐▒▒▒▄▄▒▒▒▒░░░▒▒▒▒▒▒▒▀▄▒▒
 * ░░▌░░▌█▀▒▒▒▒▒▄▀█▄▒▒▒▒▒▒▒█▒▐
 * ░▐░░░▒▒▒▒▒▒▒▒▌██▀▒▒░░░▒▒▒▀▄
 * ░▌░▒▄██▄▒▒▒▒▒▒▒▒▒░░░░░░▒▒▒▒
 * ▀▒▀▐▄█▄█▌▄░▀▒▒░░░░░░░░░░▒▒▒
 * 单身狗就这样默默地看着你，一句话也不说。
 */