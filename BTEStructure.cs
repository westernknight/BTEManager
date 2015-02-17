using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BlueTaleManager
{
    /// <summary>
    /// 通信协议结构体
    /// </summary>
    class BTEData
    {
        public int bodyLength;
        public byte[] bodyData;
    }
    public enum BTESTSCommand
    {
        STS_RETURNSTATUS = 0,
        STS_RECORD_VIDEO = 1,
        STS_SERVER_INFO = 2,
        STS_SERVER_STRESS_TEST = 1000,
    }
    public enum BTEGFSCommand
    {
        GFS_MINSSION_WORK_PERCNET = 2,
        GFS_EXCEPTION = 3,// 发生异常，运行中断
        GFS_GENERATEVIDEOREQUESTSUCCEEDED = 10,// 生成视频请求成功
        GFS_GENERATEVIDEODONE = 11, // 生成视频完成
        GFS_SERVER_STRESS_TEST_DONE = 1000,
        GFS_SERVER_STRESS_TEST_REPORT = 1001,
    }

    [Serializable]
    struct STS_RECORD_VIDEO_Struct
    {
        public bool hasContent;
        public string jasonContent;
        public string jasonPath;
        public string unity3dPath;
    }
    [Serializable]
    struct STS_SERVER_INFO_Struct
    {
        public int serverId;
    }



    [Serializable]
    struct GFS_MINSSION_WORK_PERCNET_Struct
    {
        public float percent;
    }
    [Serializable]
    struct GFS_GENERATEVIDEODONE_Struct
    {
        public string mp4Path;
    }
    [Serializable]
    struct GFS_EXCEPTION_Struct
    {
        public string reason;//出错原因
    }

    [Serializable]
    public struct GFS_SERVER_STRESS_TEST_REPORT_Struct
    {
        public int serverID;
        public string templateName;
        public float peakMemory;
        public DateTime startTime;
        public DateTime endTime;
        public int fileSize;
        public string fileName;
        public string videoDuration;
        public bool noWrong;
        public double gpuLoad;
        public double graphicsMemUse;
    }

}
