using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace TwitchTTS
{
    enum TwitchIRCStatus
    {
        Offline,
        Connecting,
        WaitingForAck,
        ConnectionFailed,
        Timeout,
        Online
    }

    class TwitchIRC 
    {
        private static Logger logger;
        public string oauth = "";
        public string nickName = "";
        public string channelName = "";
        private string server = "irc.twitch.tv";
        private int port = 6667;


        private string buffer = string.Empty;
        private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> receivedMsgs = new ConcurrentQueue<string>();

        private Thread workerThread;

        public Func<string,bool> MessageReceived = (m) => true;
        public Func<bool> IsSpeakerBusy = () => false;

        public event EventHandler<TwitchIRCStatus> ConnectionStatusChanged;

        TcpClient networkSocket;
        private TwitchIRCStatus currentStatus = TwitchIRCStatus.Offline;
        private NetworkStream networkStream;
        private StreamReader inputStream;
        private StreamWriter outputStream;
        private DateTime lastCommand = new DateTime();
        private static TimeSpan commandThrottleTimeSpan = TimeSpan.FromMilliseconds(1750);
        private CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

        internal TwitchIRCStatus CurrentStatus
        {
            get => currentStatus; set
            {
                currentStatus = value;
                ConnectionStatusChanged?.Invoke(this, value);
            }
        }

        public TwitchIRC(string nickname, string channelname, string oauthtoken)
        {
            logger = Program.LogFactory.GetLogger("irc");
            nickName = nickname;
            channelName = channelname;
            oauth = oauthtoken;
        }

        public void Start()
        {
            logger.Info("Connecting...");
            CurrentStatus = TwitchIRCStatus.Connecting;
            networkSocket = new System.Net.Sockets.TcpClient();

            networkSocket.Connect(server, port);
            if (!networkSocket.Connected)
            {
                logger.Fatal("Failed to connect!");
                return;
            }

            logger.Info("Connected!");
            CurrentStatus = TwitchIRCStatus.WaitingForAck;
            networkStream = networkSocket.GetStream();
            inputStream = new System.IO.StreamReader(networkStream);
            outputStream = new System.IO.StreamWriter(networkStream);

            workerThread = new Thread(BackgroundProcessingDoWork) { IsBackground = true, Name = "TwitchTTS Background" };
            workerThread.Start(CancellationTokenSource.Token);


            //Send PASS & NICK.
            outputStream.WriteLine("PASS " + oauth);
            outputStream.WriteLine("NICK " + nickName.ToLower());
            outputStream.WriteLine("CAP REQ :twitch.tv/tags");
            outputStream.Flush();

        }

        public void Stop()
        {
            if (workerThread != null && workerThread.IsAlive)
            {
                CancellationTokenSource.Cancel();
                if (networkSocket.Connected)
                {
                    networkSocket.Close();
                }
                workerThread.Join();
            }
        }


        public void SendCommand(string cmd)
        {
            commandQueue.Enqueue(cmd);
        }

        public void SendMsg(string msg)
        {
            commandQueue.Enqueue("PRIVMSG #" + channelName + " :" + msg);
        }

        public void SendTaggedMsg(string tagWho, string msg)
        {
            commandQueue.Enqueue("PRIVMSG #" + channelName + " :@" + tagWho + " " + msg);
        }


        private DateTime LastPing = DateTime.Now;
        public void BackgroundProcessingDoWork(object argument)
        {
            CancellationToken token = (CancellationToken)argument;
            while (!token.IsCancellationRequested)
            {
                if (networkStream.DataAvailable)
                {
                    buffer = inputStream.ReadLine();
                    logger.Debug(buffer);
                    //was message?
                    if (buffer.Contains("PRIVMSG #"))
                    {
                        //MessageReceived?.BeginInvoke(workerThread, buffer, null, null);
                        receivedMsgs.Enqueue(buffer);
                    }

                    //Send pong reply to any ping messages
                    if (buffer.StartsWith("PING "))
                    {
                        LastPing = DateTime.Now;
                        SendCommand(buffer.Replace("PING", "PONG"));
                    }

                    //After server sends 001 command, we can join a channel
                    if (buffer.Split(' ')[1] == "001")
                    {

                        SendCommand("JOIN #" + channelName);
                        CurrentStatus = TwitchIRCStatus.Online;
                    }
                }
                if (DateTime.Now - LastPing > TimeSpan.FromMinutes(8))
                {
                    CurrentStatus = TwitchIRCStatus.Timeout;
                    break;
                }
                if (CurrentStatus == TwitchIRCStatus.WaitingForAck && DateTime.Now - LastPing > TimeSpan.FromSeconds(15))
                {
                    CurrentStatus = TwitchIRCStatus.ConnectionFailed;
                    break;
                }
                if (!receivedMsgs.IsEmpty && !IsSpeakerBusy())
                {
                    while (receivedMsgs.TryDequeue(out string msg))
                    {
                        if (!MessageReceived(msg))
                        {
                            receivedMsgs.Enqueue(msg);
                            break;
                        }
                    }
                }

                if (!commandQueue.IsEmpty && DateTime.Now - lastCommand > commandThrottleTimeSpan)
                {
                    while (commandQueue.TryDequeue(out string command))
                    {
                        logger.Debug($"Sending '{command}'");
                        outputStream.WriteLine(command);
                        outputStream.Flush();
                        lastCommand = DateTime.Now;
                    }
                }

                Thread.Sleep(100);
            }
        }
    }
}
