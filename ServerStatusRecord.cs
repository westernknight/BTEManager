using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlueTaleManager
{
    class ServerStatusRecord
    {
        public struct Record_Struct
        {
            public Process process;
            public int peakMemory;
        }
        public Dictionary<int, Record_Struct> idMountProcess = new Dictionary<int, Record_Struct>();

        static ServerStatusRecord instance = null;

        public static ServerStatusRecord GetInstance()
        {
            if (instance == null)
            {
                instance = new ServerStatusRecord();
            }
            return instance;
        }
        public void AddStandalone(int serverId,Process p)
        {
            Record_Struct rs = new Record_Struct() { process = p, peakMemory = 0};

            idMountProcess.Add(serverId, rs);
            Console.WriteLine("p.Id "+p.Id);
            //线程不断拿任务
            Thread missionThread = new Thread(() =>
            {
                while (true)
                {
                    Process cmd = new Process();
                    ProcessStartInfo ps = new ProcessStartInfo();
                    ps.FileName = "cmd";
                    ps.Arguments = string.Format(@"/c @echo off && for /f ""tokens=5"" %i in ('tasklist /NH /FI ""PID eq {0}""') do echo %i ", p.Id);
                    ps.UseShellExecute = false;
                    ps.RedirectStandardOutput = true;
                    ps.CreateNoWindow = true;
                    cmd.StartInfo = ps;
                    cmd.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            int catchMem = int.Parse(e.Data.Replace(",", ""));
                            if (catchMem > rs.peakMemory)
                            {
                                rs.peakMemory = catchMem;
                                
                            }
                            Console.WriteLine(catchMem);
                        }

                    };
                    cmd.Start();
                    cmd.BeginOutputReadLine();
                    cmd.WaitForExit();
                    Thread.Sleep(1000);
                }

            });
            missionThread.Start();
        }
        public void Reset(int serverID)
        {
            Record_Struct rs = idMountProcess[serverID];
            rs.peakMemory = 0;
        }
        public int GetPeakMemory(int serverID)
        {
            Record_Struct rs = idMountProcess[serverID];
            return rs.peakMemory;
        }
        
    }
}
