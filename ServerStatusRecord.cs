using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueTaleManager
{
    class ServerStatusRecord
    {
        public struct Record_Struct
        {
            public Process process;
            public int serverID;
            public string startJson;
            public string templateName;
            public float peakMemory;
            public DateTime startTime;
            public DateTime renderDoneTime;
            public DateTime endTime;
            public int fileSize;
            public string fileName;
            public string videoDuration;
            public bool noWrong;
            public double gpuLoad;
            public double graphicsMemUse;

        }
        public List<Process> processList = new List<Process>();

        static ServerStatusRecord instance = null;

        public static ServerStatusRecord GetInstance()
        {
            if (instance == null)
            {
                instance = new ServerStatusRecord();
            }
            return instance;
        }
        public void AddStandalone(Process p,int stressInstanceCount)
        {
            Record_Struct rs = new Record_Struct();
            rs.process = p;
            rs.serverID = stressInstanceCount;

        }
        public void DoneJob(int serverId)
        {

        }
    }
}
