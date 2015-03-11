
//#define LOCAL_FILE

using GpuzDemo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tamir.SharpSsh;
//using System.Threading.Tasks;
//任务成功，但是php一直连不上，处理方法是不断尝试
//任务失败，但是php一直连不上，处理方法是丢弃任务
//server断开连接，处理方法是告诉php，任务没接收，但如果php一直连不上，处理方法是丢弃任务

namespace BlueTaleManager
{

    public class UBinder : SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            Assembly ass = Assembly.GetExecutingAssembly();
            return ass.GetType(typeName);
        }
    }
    public class Program
    {

        static GpuzWrapper gpuz = null;
        static double peakGraphicsMemory = 0;
        static double baseGraphicsMemory = 0;
        static double peakGPULoad = 0;
        static Sftp sshCp = null;

        static byte[] tmpData = new byte[1000 * 1024];

        static List<string> jsonFiles = new List<string>();
        static List<string> bundleFiles = new List<string>();
        static int jobID = 0;
        static Socket socketServer;
        static ServerIdManager jobidManager;
        static Queue<Socket> missionQueue = new Queue<Socket>();//将任务为空的socket放进队列，

        static int managerPort = 5555;
        static int serverStartId = 1;

        public static int ServerStartId
        {
            get { return Program.serverStartId; }
            set { Program.serverStartId = value; }
        }
        static string queryAddress = "http://s1/bteweb/server/query.php?work_id=";
        static string requireAddress = "http://s1/bteweb/server/requireJob.php?id=";
        static string doneAddress = "http://s1/bteweb/server/jobDone.php?job_id=";
        static string doneMp4Path = @"\\S1\htdocs\bte\Public\Video\";
        static string showPercentAddress = "http://s1/bte/Video/showpercent";
        static string checkJsonAddress = "http://s1/bte/Video/checkjson?id=";

    

        static bool debugMode = true;//是否输入测试测试
        static bool useFtpUpload = false;//是否ftp上传生成的视频
        static bool remoteFileRecipient = false;//是否接收server的文件

        //========多个实例测试硬件
        static bool stressTestMode = false;//是否测试
        static string stressInstancePath = @"R:\";
        static int stressGPULoadSensorValue = 5;
        static int stressMemUsageSensorValue = 6;
        static int stressInstanceMaxCount = 2;
        static int stressInstanceCount = 0;
        static int stressFinishInstanceCount;
        static string stressRecordFileName = "record";
        static int stressStartCount = 1;

        //========自定json测试
        static bool selfJsonTestMode = false;
        static string selfJsonPath = "";
        static int selfJsonFinishCount = 0;
        static LitJson.JsonData selfJsonData = null;

        static Dictionary<Socket, bool> allSockets = new Dictionary<Socket, bool>();



        static BTEGFSCommand ParseHeader(byte[] raw)
        {
            byte[] packageHeader = new byte[4];

            packageHeader[0] = raw[0];
            packageHeader[1] = raw[1];
            packageHeader[2] = raw[2];
            packageHeader[3] = raw[3];
            return (BTEGFSCommand)BitConverter.ToInt32(packageHeader, 0);
        }
        static byte[] UnPack(byte[] raw)
        {
            byte[] dest = new byte[raw.Length - 4];
            Buffer.BlockCopy(raw, 4, dest, 0, raw.Length - 4);

            return dest;
        }

        static void DealPackage(Socket ts, byte[] body_data)
        {
            BTEData dataSave = new BTEData() { bodyLength = body_data.Length, bodyData = body_data };
            switch (ParseHeader(dataSave.bodyData))
            {
                case BTEGFSCommand.GFS_MINSSION_WORK_PERCNET:
                    {
                        if (gpuz != null)
                        {
                            if (peakGraphicsMemory < gpuz.SensorValue(stressMemUsageSensorValue))
                            {
                                peakGraphicsMemory = gpuz.SensorValue(stressMemUsageSensorValue);
                            }
                            if (peakGPULoad < gpuz.SensorValue(stressGPULoadSensorValue))
                            {
                                peakGPULoad = gpuz.SensorValue(stressGPULoadSensorValue);
                            }
                        }

                        Console.WriteLine("BTEGFSCommand.GFS_MINSSION_WORK_PERCNET");
                        IFormatter formatter = new BinaryFormatter();
                        formatter.Binder = new UBinder();
                        MemoryStream ms = new MemoryStream(UnPack(dataSave.bodyData));
                        GFS_MINSSION_WORK_PERCNET_Struct obj = (GFS_MINSSION_WORK_PERCNET_Struct)formatter.Deserialize(ms);

                        Console.WriteLine("job percent: " + string.Format("{0:N1}", obj.percent * 100));

                        try
                        {
                            if (debugMode == false && selfJsonTestMode == false && stressTestMode == false)
                            {
                                int id = jobidManager.GetSocketJobId(ts);
                                IDictionary<string, string> parameters = new Dictionary<string, string>();
                                parameters.Add("job_id", id.ToString());
                                parameters.Add("info", string.Format("{0:N1}", obj.percent * 90));
                                var response = HttpWebResponseUtility.CreatePostHttpResponse(showPercentAddress, parameters, null, null, Encoding.UTF8, null);
                                if (response != null)
                                {
                                    Stream _str = response.GetResponseStream();
                                    StreamReader _strd = new StreamReader(_str);
                                    string html = _strd.ReadToEnd();
                                    response.Close();
                                }
                                else
                                {
                                    response.Close();
                                }
                            }
                        }
                        catch (Exception ex)
                        {

                            Console.WriteLine(ex);
                        }
                    }
                    break;

                case BTEGFSCommand.GFS_EXCEPTION:
                    {
                        Console.WriteLine("BTEGFSCommand.GFS_EXCEPTION");
                        IFormatter formatter = new BinaryFormatter();
                        formatter.Binder = new UBinder();
                        MemoryStream ms = new MemoryStream(UnPack(dataSave.bodyData));
                        GFS_EXCEPTION_Struct obj = (GFS_EXCEPTION_Struct)formatter.Deserialize(ms);
                        Console.WriteLine("reason " + obj.reason);
                        TryTellPhpServerJobDone(ts, dataSave, true);
                    }

                    break;
                case BTEGFSCommand.GFS_GENERATEVIDEOREQUESTSUCCEEDED:
                    Console.WriteLine("BTEGFSCommand.GFS_GENERATEVIDEOREQUESTSUCCEEDED");
                    peakGraphicsMemory = 0;
                    peakGPULoad = 0;
                    break;
                case BTEGFSCommand.GFS_GENERATEVIDEODONE:
                    {
                        Console.WriteLine("BTEGFSCommand.GFS_GENERATEVIDEODONE");
                        IFormatter formatter = new BinaryFormatter();
                        formatter.Binder = new UBinder();
                        MemoryStream ms = new MemoryStream(UnPack(dataSave.bodyData));
                        GFS_GENERATEVIDEODONE_Struct obj = (GFS_GENERATEVIDEODONE_Struct)formatter.Deserialize(ms);

                        Console.WriteLine("[server id] " + obj.serverID);
                        Console.WriteLine("[jobID] " + obj.jobID);
                        Console.WriteLine("[templateName] " + obj.templateName);
                        Console.WriteLine("[startTime] " + obj.startTime);
                        Console.WriteLine("[renderDoneTime] " + obj.renderDoneTime);
                        Console.WriteLine("[endTime] " + obj.endTime);
                        Console.WriteLine("[fileSize] " + obj.fileSize);
                        Console.WriteLine("[videoDuration] " + obj.videoDuration);
                        TryTellPhpServerJobDone(ts, dataSave, false);
                    }
                    break;
                case BTEGFSCommand.GFS_SERVER_STRESS_TEST_REPORT://任务完成
                    {
                        Console.WriteLine("BTEGFSCommand.GFS_SERVER_STRESS_TEST_REPORT");

                        IFormatter formatter = new BinaryFormatter();
                        formatter.Binder = new UBinder();
                        MemoryStream ms = new MemoryStream(UnPack(dataSave.bodyData));
                        GFS_SERVER_STRESS_TEST_REPORT_Struct obj = (GFS_SERVER_STRESS_TEST_REPORT_Struct)formatter.Deserialize(ms);



                        string info = string.Format("{0},{1},{2},(startTime){3},(renderDoneTime){4},(endTime){5},(generateDeltaTime){6},(renderDeltaTime){7},(ffmpegDeltaTime){8},(fileSize){9:N2},(videoDuration){10},(peakMemory){11:N2},(loadBundleTime){12},(peakGraphicsMemory){13},(peakGPULoad){14}", stressInstanceCount, obj.serverID, obj.templateName, obj.startTime, obj.renderDoneTime, obj.endTime, (obj.endTime - obj.startTime).TotalSeconds, (obj.renderDoneTime - obj.startTime).TotalSeconds, (obj.endTime - obj.renderDoneTime).TotalSeconds, (float)obj.fileSize / 1024 / 1024, obj.videoDuration, obj.peakMemory / 1024, obj.loadBundleTime.TotalSeconds, peakGraphicsMemory - baseGraphicsMemory, peakGPULoad);


                        Process.Start("cmd", string.Format("/c echo {0}>>{1}.txt", info, stressRecordFileName));
                        Console.WriteLine(info);
                        Console.WriteLine();
                    }
                    break;
                case BTEGFSCommand.GFS_SERVER_STRESS_TEST_DONE://所有任务完成
                    {
                        Console.WriteLine("BTEGFSCommand.GFS_SERVER_STRESS_TEST_DONE");
                        Process.Start("cmd", string.Format("/c echo,>>{0}.txt", stressRecordFileName));
                        stressFinishInstanceCount++;
                        if (stressFinishInstanceCount == stressInstanceCount)
                        {
                            stressFinishInstanceCount = 0;

                            StartInstance();
                        }

                        //socket stress done 
                        allSockets[ts] = true;


                    }
                    break;
                default:
                    Console.WriteLine("BTEGFSCommand.Know");
                    break;
            }
        }
        static void ReceivePackage(Socket ts, int receiveLength, byte[] receiveBuffer, int needBodyLength, byte[] allocBuffer)
        {
            if (needBodyLength > receiveLength - 4)//不完整包
            {
                Buffer.BlockCopy(receiveBuffer, 4, allocBuffer, 0, receiveLength - 4);//把所有数据放进Buffer,不包含包的前4字节
                int fillLength = receiveLength - 4;

                while (true)
                {
                    //收到包截断的情况，继续接收
                    if (fillLength != needBodyLength)
                    {
                        int body_part = ts.Receive(tmpData, 0, tmpData.Length, SocketFlags.None);

                        if (fillLength + body_part > needBodyLength)
                        {
                            //粘包
                            int visioLength = (fillLength + body_part) - needBodyLength;//粘包长度

                            Buffer.BlockCopy(tmpData, 0, allocBuffer, fillLength, body_part - visioLength);
                            DealPackage(ts, allocBuffer);
                            byte[] visioBuffer = new byte[visioLength];
                            Buffer.BlockCopy(tmpData, needBodyLength - fillLength, visioBuffer, 0, visioLength);
                            VisioPackage(ts, visioBuffer);
                            break;
                        }
                        Buffer.BlockCopy(tmpData, 0, allocBuffer, fillLength, body_part);

                        fillLength += body_part;
                    }
                    else
                    {
                        DealPackage(ts, allocBuffer);
                        break;
                    }
                }

            }
            else if (needBodyLength == receiveLength - 4)
            {
                Buffer.BlockCopy(receiveBuffer, 4, allocBuffer, 0, needBodyLength);
                DealPackage(ts, allocBuffer);
            }
            else//粘包
            {
                Buffer.BlockCopy(receiveBuffer, 4, allocBuffer, 0, needBodyLength);
                DealPackage(ts, allocBuffer);
                int visioLength = receiveLength - (needBodyLength + 4);//粘包长度
                byte[] visioBuffer = new byte[visioLength];

                Buffer.BlockCopy(receiveBuffer, 4 + needBodyLength, visioBuffer, 0, visioLength);
                VisioPackage(ts, visioBuffer);
            }
        }

        static void VisioPackage(Socket ts, byte[] visioBuffer)
        {
            if (visioBuffer.Length >= 4)//can read package size
            {
                byte[] packageHeader = new byte[4];
                packageHeader[0] = visioBuffer[0];
                packageHeader[1] = visioBuffer[1];
                packageHeader[2] = visioBuffer[2];
                packageHeader[3] = visioBuffer[3];
                int bodyLength = BitConverter.ToInt32(packageHeader, 0);
                byte[] bodyData = new byte[bodyLength];

                if (visioBuffer.Length >= bodyLength + 4)
                {
                    ReceivePackage(ts, visioBuffer.Length, visioBuffer, bodyLength, bodyData);
                }
                else
                {
                    int body_part = ts.Receive(tmpData, 0, tmpData.Length, SocketFlags.None);
                    byte[] receiveBuffer = new byte[visioBuffer.Length + body_part];

                    Buffer.BlockCopy(visioBuffer, 0, receiveBuffer, 0, visioBuffer.Length);
                    Buffer.BlockCopy(tmpData, 0, receiveBuffer, visioBuffer.Length, body_part);

                    ReceivePackage(ts, receiveBuffer.Length, receiveBuffer, bodyLength, bodyData);
                }

            }
            else//cant read package size,must read next pocket
            {
                int body_part = ts.Receive(tmpData, 0, tmpData.Length, SocketFlags.None);
                byte[] receiveBuffer = new byte[visioBuffer.Length + body_part];

                Buffer.BlockCopy(visioBuffer, 0, receiveBuffer, 0, visioBuffer.Length);
                Buffer.BlockCopy(tmpData, 0, receiveBuffer, visioBuffer.Length, body_part);

                byte[] packageHeader = new byte[4];
                packageHeader[0] = receiveBuffer[0];
                packageHeader[1] = receiveBuffer[1];
                packageHeader[2] = receiveBuffer[2];
                packageHeader[3] = receiveBuffer[3];
                int bodyLength = BitConverter.ToInt32(packageHeader, 0);
                byte[] bodyData = new byte[bodyLength];

                ReceivePackage(ts, receiveBuffer.Length, receiveBuffer, bodyLength, bodyData);
            }
        }
        static void ReceiveCallback(IAsyncResult result)
        {
            Socket ts = (Socket)result.AsyncState;
            try
            {

                int c = ts.EndReceive(result);

                result.AsyncWaitHandle.Close();
                if (c == 0)
                {
                    ServerDisconnect(ts);
                }
                else
                {
                    byte[] packageHeader = new byte[4];
                    packageHeader[0] = tmpData[0];
                    packageHeader[1] = tmpData[1];
                    packageHeader[2] = tmpData[2];
                    packageHeader[3] = tmpData[3];

                    int bodyLength = BitConverter.ToInt32(packageHeader, 0);
                    byte[] bodyData = new byte[bodyLength];
                    ReceivePackage(ts, c, tmpData, bodyLength, bodyData);
                    ts.BeginReceive(tmpData, 0, tmpData.Length, SocketFlags.None, ReceiveCallback, ts);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ServerDisconnect(ts);
            }
        }


        private static void ServerDisconnect(Socket ts)
        {
            try
            {
                jobidManager.CloseSocket(ts);
                allSockets.Remove(ts);

                stressInstanceCount--;
                ts.Disconnect(false);
                Console.WriteLine("客户端已断开连接");
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex);
            }


        }

        private static void TryTellPhpServerJobDone(Socket ts, BTEData data, bool error)
        {

            int jobID = jobidManager.GetSocketJobId(ts);

            if (debugMode || stressTestMode || selfJsonTestMode)
            {
                ServerSocketIsCasual(ts);
            }
            else
            {
                if (error)
                {
                    //to do 
                    //return;
                    Console.WriteLine("error id: " + jobID);
                    ServerSocketIsCasual(ts);
                }
                else
                {
                    if (jobID != -1)
                    {
                        try
                        {
                            IFormatter formatter = new BinaryFormatter();
                            formatter.Binder = new UBinder();
                            MemoryStream ms = new MemoryStream(UnPack(data.bodyData));
                            GFS_GENERATEVIDEODONE_Struct obj = (GFS_GENERATEVIDEODONE_Struct)formatter.Deserialize(ms);

                            Console.WriteLine("done mp4 path " + obj.mp4Path);
                            CopyVideoFile(obj.mp4Path);


                           
                                int id = jobidManager.GetSocketJobId(ts);
                                IDictionary<string, string> parameters = new Dictionary<string, string>();
                                parameters.Add("job_id", id.ToString());
                                parameters.Add("info", string.Format("{0:N1}", 100));
                                var response = HttpWebResponseUtility.CreatePostHttpResponse(showPercentAddress, parameters, null, null, Encoding.UTF8, null);
                                if (response != null)
                                {
                                    Stream _str = response.GetResponseStream();
                                    StreamReader _strd = new StreamReader(_str);
                                    string html = _strd.ReadToEnd();
                                    response.Close();
                                }
                                else
                                {
                                    response.Close();
                                }
                            


                            string jobDone = doneAddress + jobID.ToString();
                            HttpWebRequest _HttpWebRequest = HttpWebRequest.Create(jobDone) as HttpWebRequest;
                            _HttpWebRequest.Method = "GET";
                            using (WebResponse _WebResponse = _HttpWebRequest.GetResponse())
                            {
                                Stream _Stream = _WebResponse.GetResponseStream();
                                using (StreamReader _StreamReader = new StreamReader(_Stream))
                                {
                                    string _str = _StreamReader.ReadToEnd();
                                    Console.WriteLine(_str);
                                    ServerSocketIsCasual(ts);
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                            Task.Factory.StartNew(() =>
                            {
                                TryTellPhpServerJobDone(ts, data, error);
                            });

                        }
                    }
                    else
                    {
                        Console.WriteLine("socket mount jobid encorrect"); ;
                    }
                }

            }

        }

        private static void ServerSocketIsCasual(Socket ts)
        {
            jobidManager.CleanSocketJobId(ts);
            if (missionQueue.Contains(ts) == false)
            {

                missionQueue.Enqueue(ts);
            }
            else
            {
                Console.WriteLine("socket has already in queue,code mistake");
            }
        }


        /// <summary>
        /// /
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        static byte[] BuildPack(byte[] a, byte[] b)
        {
            byte[] c = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, c, 0, a.Length);
            Buffer.BlockCopy(b, 0, c, a.Length, b.Length);
            return c;
        }
        static void CopyVideoFile(string videoFilepath)
        {
            if (debugMode)
            {
                return;
            }
            try
            {

                string destFilepath = doneMp4Path + Path.GetFileName(videoFilepath);
                string videoFilepath2 = videoFilepath.Replace(".mp4", ".webm");
                Console.WriteLine(videoFilepath2);
                string destFilepath2 = doneMp4Path + Path.GetFileName(videoFilepath2);

                if (remoteFileRecipient)
                {

                    FileInfo fiMp4 = new FileInfo(Path.GetFileName(videoFilepath));
                    if (fiMp4.Exists)
                    {
                        File.Copy(Path.GetFileName(videoFilepath), destFilepath, true);
                    }
                    FileInfo fiWebm = new FileInfo(Path.GetFileName(videoFilepath2));
                    if (fiWebm.Exists)
                    {
                        File.Copy(Path.GetFileName(videoFilepath2), destFilepath2, true);
                    }
                }
                else
                {
                    File.Copy(videoFilepath, destFilepath, true);
                    File.Copy(videoFilepath2, destFilepath2, true);

                    if (useFtpUpload)
                    {
                        try
                        {
                            DateTime copystart = DateTime.Now;
                            Console.WriteLine("copy to ftp start");
                            sshCp.Put(videoFilepath, string.Format("../webapp/app/Public/Video/{0}", Path.GetFileName(videoFilepath)));
                            sshCp.Put(videoFilepath2, string.Format("../webapp/app/Public/Video/{0}", Path.GetFileName(videoFilepath2)));
                            Console.WriteLine("copy span time: " + (DateTime.Now - copystart).TotalSeconds + "s");
                        }
                        catch (Exception e)
                        {

                            Console.WriteLine(e);
                        }
                    }
                    
                }




            }
            catch (Exception ex)
            {

                Console.WriteLine(ex);
            }



        }

        static void SendWithLength(Socket ts, int cmd, object data = null)
        {
            try
            {
                if (data == null)
                {

                    byte[] send_data_with_length = BuildPack(BitConverter.GetBytes(BitConverter.GetBytes(cmd).Length), BitConverter.GetBytes(cmd));
                    //ts.BeginSend(send_data_with_length, 0, send_data_with_length.Length, SocketFlags.None, new AsyncCallback(SendCallback), ts);
                    ts.Send(send_data_with_length);
                }
                else
                {
                    MemoryStream stream = new MemoryStream();
                    IFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(stream, data);

                    byte[] send_data = BuildPack(BitConverter.GetBytes(cmd), stream.ToArray());//带命令数据包
                    byte[] send_data_with_length = BuildPack(BitConverter.GetBytes(send_data.Length), send_data);
                    //ts.BeginSend(send_data_with_length, 0, send_data_with_length.Length, SocketFlags.None, new AsyncCallback(SendCallback), ts);
                    ts.Send(send_data_with_length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

        }

        static void SendCallback(IAsyncResult ar)
        {
            Socket ts = (Socket)ar.AsyncState;
            try
            {

                ts.EndSend(ar);
                ar.AsyncWaitHandle.Close();
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex);
            }
        }


        static void GetSelfJsonMission(Socket ts)
        {
            DirectoryInfo di = new DirectoryInfo(selfJsonPath);
            FileInfo[] files = di.GetFiles();

            List<FileInfo> jsonFiles = new List<FileInfo>();
            foreach (var item in files)
            {
                if (item.Extension == ".json")
                {
                    jsonFiles.Add(item);
                }
            }
            if (jsonFiles.Count != 0)
            {

            }
            if (selfJsonFinishCount < jsonFiles.Count)
            {
                using (StreamReader sr = new StreamReader(jsonFiles[selfJsonFinishCount].OpenRead()))
                {
                    string json = sr.ReadToEnd();
                    selfJsonData = LitJson.JsonMapper.ToObject(json);
                    selfJsonData["id"] = Path.GetFileNameWithoutExtension(jsonFiles[selfJsonFinishCount].Name);
                    STS_RECORD_VIDEO_Struct str = new STS_RECORD_VIDEO_Struct()
                    {
                        hasContent = true,

                        jasonContent = selfJsonData.ToJson(),
                    };
                    SendWithLength(ts, (int)(BTESTSCommand.STS_RECORD_VIDEO), str);
                }
                selfJsonFinishCount++;
            }
            else if (jsonFiles.Count != 0)
            {
                selfJsonFinishCount = 0;
                Console.WriteLine("all json has finish,press any key to restart...");
                Console.ReadKey();
                using (StreamReader sr = new StreamReader(jsonFiles[selfJsonFinishCount].OpenRead()))
                {
                    string json = sr.ReadToEnd();
                    selfJsonData = LitJson.JsonMapper.ToObject(json);
                    selfJsonData["id"] = Path.GetFileNameWithoutExtension(jsonFiles[selfJsonFinishCount].Name);
                    STS_RECORD_VIDEO_Struct str = new STS_RECORD_VIDEO_Struct()
                    {
                        hasContent = true,

                        jasonContent = selfJsonData.ToJson(),
                    };
                    SendWithLength(ts, (int)(BTESTSCommand.STS_RECORD_VIDEO), str);
                }
                selfJsonFinishCount++;
            }
        }
        static void GetDebugModeMission(Socket ts)
        {
            try
            {

                int jobID = 0;
                while (true)
                {
                    Console.WriteLine("input jobid:");
                    try
                    {
                        jobID = int.Parse(Console.ReadLine());
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);

                    }
                }

                if (ts.Connected == true)
                {
                    jobidManager.MoundJobId(ts, jobID);
                    string url = checkJsonAddress + jobID.ToString();
                    HttpWebRequest _HttpWebRequest = HttpWebRequest.Create(url) as HttpWebRequest;
                    _HttpWebRequest.Method = "GET";
                    using (WebResponse _WebResponse = _HttpWebRequest.GetResponse())
                    {
                        Stream _Stream = _WebResponse.GetResponseStream();
                        using (StreamReader _StreamReader = new StreamReader(_Stream))
                        {
                            string json_string = _StreamReader.ReadToEnd();
                            if (string.IsNullOrEmpty(json_string))
                            {
                                Console.WriteLine("json id is null");
                                ServerSocketIsCasual(ts);
                            }
                            else
                            {
                                try
                                {
                                    LitJson.JsonData json;
                                    json = LitJson.JsonMapper.ToObject(json_string);
                                    json["id"] = jobID.ToString();
                                    STS_RECORD_VIDEO_Struct str2 = new STS_RECORD_VIDEO_Struct()
                                    {
                                        hasContent = true,
                                        jasonContent = json.ToJson()
                                    };

                                    Console.WriteLine(str2.jasonContent);
                                    SendWithLength(ts, (int)(BTESTSCommand.STS_RECORD_VIDEO), str2);
                                }
                                catch (Exception ex)
                                {

                                    Console.WriteLine(json_string);
                                    Console.WriteLine(ex);
                                    ServerSocketIsCasual(ts);
                                    return;
                                }
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ServerSocketIsCasual(ts);
            }
        }
        static void GetMission(Socket ts)
        {
            try
            {
                string jobString = queryAddress + jobidManager.GetSocketWorkId(ts);
                Console.WriteLine("job: " + jobString);
                HttpWebRequest _HttpWebRequest = HttpWebRequest.Create(jobString) as HttpWebRequest;
                _HttpWebRequest.Method = "GET";

                using (WebResponse _WebResponse = _HttpWebRequest.GetResponse())
                {
                    Stream _Stream = _WebResponse.GetResponseStream();
                    using (StreamReader _StreamReader = new StreamReader(_Stream))
                    {

                        STS_RECORD_VIDEO_Struct str = new STS_RECORD_VIDEO_Struct()
                        {
                            hasContent = true,

                            jasonContent = _StreamReader.ReadToEnd(),
                        };
                        if (string.IsNullOrEmpty(str.jasonContent))
                        {
                            Console.WriteLine("null job");
                            Thread.Sleep(5000);
                            if (ts.Connected)
                            {
                                ServerSocketIsCasual(ts);
                            }
                        }
                        else
                        {
                            SendMission(ts, str);

                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                if (ts.Connected)
                {
                    ServerSocketIsCasual(ts);
                }
            }
        }

        private static void SendMission(Socket ts, STS_RECORD_VIDEO_Struct str)
        {
            try
            {
                LitJson.JsonData json;
                json = LitJson.JsonMapper.ToObject(str.jasonContent);


                jobID = int.Parse((string)json["id"]);
                Console.WriteLine("jobID: " + jobID.ToString());
                jobidManager.MoundJobId(ts, jobID);

                string url = requireAddress + jobID.ToString();
                HttpWebRequest _HttpWebRequest = HttpWebRequest.Create(url) as HttpWebRequest;
                _HttpWebRequest.Method = "GET";
                using (WebResponse _WebResponse = _HttpWebRequest.GetResponse())
                {
                    Stream _Stream = _WebResponse.GetResponseStream();
                    using (StreamReader _StreamReader = new StreamReader(_Stream))
                    {
                    }
                }

                SendWithLength(ts, (int)(BTESTSCommand.STS_RECORD_VIDEO), str);
                Console.WriteLine(str.jasonContent);
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                //发送失败很可能是客户端断开连接，断开连接已有处理方法
                Console.WriteLine("发送任务失败");
                Console.WriteLine(str.jasonContent);

                Console.WriteLine(ex);
                ServerSocketIsCasual(ts);
            }
        }
        static void StartInstance()
        {
            if (stressInstanceCount < stressInstanceMaxCount)
            {
                //string exePathDir = stressPath+"\\server"+stressInstanceCount;
                string exePathDir = stressInstancePath;
                string exeName = "bteserver_d3d11.exe";
                Process standalone = Process.Start("cmd", string.Format("/c cd /d {0} && {1}", exePathDir, exeName));

                Console.WriteLine(string.Format("/c cd /d {0} && {1}", exePathDir, exeName));
            }
        }
        static void CheckKeys(LitJson.JsonData arguments)
        {
           
        }
        static void Main(string[] args)
        {
            bool testftp = false;
            if (testftp)//是否测试ftp
            {
                sshCp = new Sftp("115.28.191.93", "root", "Bluearc310");
                sshCp.Connect();
                Console.WriteLine("ftp connect: " + sshCp.Connected);

                DateTime copystart = DateTime.Now;
                sshCp.Put("d:/11056.mp4", string.Format("../webapp/app/Public/Video/{0}", "11056.mp4"));
                Console.WriteLine("copy span time: " + (DateTime.Now - copystart).TotalSeconds + "s");
                return;
            }     


            try
            {
                FileInfo fi = new FileInfo("manager.inf");
                string jsonContent;
                using (StreamReader sr = new StreamReader(fi.Open(FileMode.OpenOrCreate)))
                {
                    jsonContent = sr.ReadToEnd();
                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        LitJson.JsonData arguments = new LitJson.JsonData();

                        
                        arguments["managerPort"] = managerPort;
                        arguments["serverStartId"] = ServerStartId;
                        arguments["queryAddress"] = queryAddress;
                        arguments["requireAddress"] = requireAddress;
                        arguments["doneAddress"] = doneAddress;
                        arguments["showPercentAddress"] = showPercentAddress;
                        arguments["checkJsonAddress"] = checkJsonAddress;
                        arguments["doneMp4Path"] = doneMp4Path;
                        arguments["debugMode"] = debugMode;
                        arguments["useFtpUpload"] = useFtpUpload;
                        arguments["remoteFileRecipient"] = remoteFileRecipient;
                        arguments["stressTest"] = stressTestMode;
                        arguments["stressStartCount"] = stressStartCount;
                        arguments["stressPath"] = stressInstancePath;
                        arguments["stressGPULoadSensorValue"] = stressGPULoadSensorValue;
                        arguments["stressGPUZMemUsageSensorValue"] = stressMemUsageSensorValue;
                        arguments["stressInstanceMaxCount"] = stressInstanceMaxCount;
                        arguments["selfJsonTestMode"] = selfJsonTestMode;
                        arguments["selfJsonPath"] = selfJsonPath;


                        jsonContent = arguments.ToJson();
                    }
                    else
                    {
                        LitJson.JsonData arguments = LitJson.JsonMapper.ToObject(jsonContent);
                        
                        managerPort = (int)arguments["managerPort"];
                        ServerStartId = (int)arguments["serverStartId"];
                        queryAddress = (string)arguments["queryAddress"];
                        requireAddress = (string)arguments["requireAddress"];
                        doneAddress = (string)arguments["doneAddress"];
                        showPercentAddress = (string)arguments["showPercentAddress"];
                        checkJsonAddress = (string)arguments["checkJsonAddress"];
                        doneMp4Path = (string)arguments["doneMp4Path"];
                        debugMode = (bool)arguments["debugMode"];
                        useFtpUpload = (bool)arguments["useFtpUpload"];
                        remoteFileRecipient = (bool)arguments["remoteFileRecipient"];
                        stressInstancePath = (string)arguments["stressPath"];
                        stressTestMode = (bool)arguments["stressTest"];

                        stressGPULoadSensorValue = (int)arguments["stressGPULoadSensorValue"];
                        stressMemUsageSensorValue = (int)arguments["stressGPUZMemUsageSensorValue"];


                        stressInstanceMaxCount = (int)arguments["stressInstanceMaxCount"];
                        selfJsonTestMode = (bool)arguments["selfJsonTestMode"];
                        selfJsonPath = (string)arguments["selfJsonPath"];
                        stressStartCount = (int)arguments["stressStartCount"];
                    }
                    Console.WriteLine("managerPort: " + managerPort);
                    Console.WriteLine("serverStartId: " + ServerStartId);
                    Console.WriteLine("queryAddress: " + queryAddress);
                    Console.WriteLine("requireAddress: " + requireAddress);
                    Console.WriteLine("doneAddress: " + doneAddress);
                    Console.WriteLine("showPercentAddress: " + showPercentAddress);
                    Console.WriteLine("checkJsonAddress: " + checkJsonAddress);
                    Console.WriteLine("doneMp4Path: " + doneMp4Path);
                    Console.WriteLine("debugMode: " + debugMode);
                    Console.WriteLine("useFtpUpload: " + useFtpUpload);
                    Console.WriteLine("stressInstancePath: " + stressInstancePath);
                    Console.WriteLine("stressTestMode: " + stressTestMode);
                    Console.WriteLine("stressInstanceMaxCount: " + stressInstanceMaxCount);
                    Console.WriteLine("stressStartCount: " + stressStartCount);
                    Console.WriteLine("stressGPULoadSensorValue: " + stressGPULoadSensorValue);
                    Console.WriteLine("stressMemUsageSensorValue: " + stressMemUsageSensorValue);
                    Console.WriteLine("remoteFileRecipient: " + remoteFileRecipient);
                    Console.WriteLine("selfJsonTestMode: " + selfJsonTestMode);
                    Console.WriteLine("selfJsonPath: " + selfJsonPath);

                }
                using (StreamWriter sw = new StreamWriter(fi.OpenWrite()))
                {
                    sw.WriteLine(jsonContent);
                }
                jobidManager = new ServerIdManager();

                socketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socketServer.Bind(new IPEndPoint(IPAddress.Any, managerPort));
                socketServer.Listen(int.MaxValue);
                if (useFtpUpload)
                {
                    sshCp = new Sftp("115.28.191.93", "root", "Bluearc310");
                    sshCp.Connect();
                    Console.WriteLine("ftp connect: " + sshCp.Connected);
                }
               
                Console.WriteLine("服务端已启动，等待连接...");


                socketServer.BeginAccept((ar) =>
                {
                    AcceptCallback(ar);
                }, socketServer);

                if (stressTestMode)//如果是测试，启动server,让server自己从磁盘获得json
                {
                    try
                    {
                        gpuz = new GpuzWrapper();
                        gpuz.Open();
                        baseGraphicsMemory = gpuz.SensorValue(stressMemUsageSensorValue);
                    }
                    catch (Exception ex)
                    {

                        Console.WriteLine(ex);
                    }

                    stressRecordFileName += System.Environment.TickCount.ToString();
                    stressFinishInstanceCount = 0;
                    for (int i = 0; i < stressStartCount; i++)
                    {
                        StartInstance();
                    }


                }
                else if (selfJsonTestMode)
                {
                    Thread missionThread = new Thread(() =>
                    {
                        while (true)
                        {
                            if (missionQueue.Count > 0)
                            {
                                Socket ts = missionQueue.Dequeue();

                                GetSelfJsonMission(ts);

                            }
                            Thread.Sleep(50);
                        }

                    });
                    missionThread.Start();
                }
                else if (debugMode)
                {
                    Thread missionThread = new Thread(() =>
                    {
                        while (true)
                        {
                            if (missionQueue.Count > 0)
                            {
                                Socket ts = missionQueue.Dequeue();

                                GetDebugModeMission(ts);

                            }
                            Thread.Sleep(50);
                        }

                    });
                    missionThread.Start();
                }
                else//否则，从数据库过得json
                {
                    Thread missionThread = new Thread(() =>
                    {
                        while (true)
                        {
                            if (missionQueue.Count > 0)
                            {
                                Socket ts = missionQueue.Dequeue();

                                GetMission(ts);

                            }
                            Thread.Sleep(50);
                        }

                    });
                    missionThread.Start();
                }
                //接收连接
                while (true)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("请按任意键继续. . .");
                Console.ReadKey();
            }
        }
        private static void AcceptCallback(IAsyncResult ar)
        {
            try
            {

                Socket ts = socketServer.EndAccept(ar);
                ar.AsyncWaitHandle.Close();
                Console.WriteLine("客户端已连接");
                allSockets.Add(ts, true);


                int workid = jobidManager.GetSocketWorkId(ts);

                STS_SERVER_INFO_Struct data = new STS_SERVER_INFO_Struct() { serverId = workid };
                SendWithLength(ts, (int)BTESTSCommand.STS_SERVER_INFO, data);

                ts.BeginReceive(tmpData, 0, tmpData.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), ts);
                ServerSocketIsCasual(ts);

                if (stressTestMode)
                {
                    stressInstanceCount++;

                    foreach (KeyValuePair<Socket, bool> kvp in allSockets)
                    {
                        if (kvp.Value)
                        {
                            SendWithLength(kvp.Key, (int)BTESTSCommand.STS_SERVER_STRESS_TEST);
                        }
                    }
                    allSockets[ts] = false;
                }


                socketServer.BeginAccept((ar2) =>
                {
                    AcceptCallback(ar2);

                }, socketServer);


            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
