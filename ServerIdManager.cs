using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
//using System.Threading.Tasks;

namespace BlueTaleManager
{
    public class ServerIdManager
    {
        Dictionary<Socket, int> socket2WorkId = new Dictionary<Socket, int>();
        Dictionary<Socket, int> socket2JobId = new Dictionary<Socket, int>();
        Stack<int> idStack = new Stack<int>();

        public ServerIdManager()
        {
            // work id
            //最大1000个server
            for (int i = 10000000; i >=0 ; i--)
            {
                idStack.Push(i + Program.ServerStartId);
            }           


        }
        /// <summary>
        /// input socket and get the work id
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public int GetSocketWorkId(Socket s)
        {
            try
            {
                if (socket2WorkId.ContainsKey(s))
                {
                    return socket2WorkId[s];
                }
                int id = idStack.Pop();
                socket2WorkId.Add(s, id);
                return id;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }
        public void MoundJobId(Socket s, int jobId)
        {
            if (socket2JobId.ContainsKey(s))
            {
                Console.WriteLine("error socket {0} has already mounted!!!!!!!!!", s);
            }
            else
            {
                socket2JobId.Add(s, jobId);
            }
        }
        public void CleanSocketJobId(Socket s)
        {
            if (socket2JobId.ContainsKey(s))
            {
                socket2JobId.Remove(s);
            }
            
        }
        public int GetSocketJobId(Socket s)
        {

            if (socket2JobId.ContainsKey(s))
            {
                int jobid = socket2JobId[s];

                return jobid;
            }
            else
            {
                return -1;
            }

        }

        public void CloseSocket(Socket s)
        {
            if (socket2WorkId.ContainsKey(s))
            {
                int id = socket2WorkId[s];
                idStack.Push(id);
                socket2WorkId.Remove(s);
            }
        }
    }
}
