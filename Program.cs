using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Timers;

[assembly: AssemblyVersionAttribute("1.0.0.0")]
namespace DotNetStratumMiner
{
    class Program
    {
        private static Miner CoinMiner;
        private static Validator CoinValidator;
        private static int CurrentDifficulty;
        private static Queue<Job> IncomingJobs = new Queue<Job>();
        private static Stratum stratum;
        private static BackgroundWorker worker, validatorWorker;
        private static int SharesSubmitted = 0;
        private static int SharesAccepted = 0;
        private static string Server = "";
        private static int Port = 0;
        private static string Username = "";
        private static string Password = "";
        private static string ComName = "";

        private static System.Timers.Timer KeepaliveTimer;
        
        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            Console.Write("Keepalive - ");
            stratum.SendAUTHORIZE();
        }

        // https://www.microsoft.com/en-us/download/confirmation.aspx?id=26999
        static void Main(string[] args)
        {
            string ExecutableName = System.Environment.GetCommandLineArgs()[0];
            string CommandOptions = Environment.CommandLine.Replace(ExecutableName, "").Replace("\"", "").Trim();
            //CommandOptions = "-o sha256.poolbinance.com:443 -u askyMiner.001 -p x -c COM3";
            CommandOptions = CommandOptions.Replace("-o ", "-o").Replace("-u ", "-u").Replace("-p ", "-p").Replace("-c ", "-c");
            string[] Options = CommandOptions.Split(' ');

            if (CommandOptions.Length == 0 || Options[0] == "-h")
            {
                Console.WriteLine("-o URL           URL of mining server (e.g. tcp://megahash.wemineltc.com:3333)");
                Console.WriteLine("-u USERNAME      Username for mining server");
                Console.WriteLine("-p PASSWORD      Password for mining server");
                Console.WriteLine("-c serial COM    Serial COM name (e.g. COM3)");
                Console.WriteLine("-h               Display this help text and exit");
                Environment.Exit(-1);
            }

            foreach (string arg in Options)
            {
                switch(arg.Substring(0, 2))
                {
                    case "-o":
                        if (!arg.Contains(":"))
                        {
                            Console.WriteLine("Missing server port. URL should be in the format of tcp://megahash.wemineltc.com:3333");
                            Environment.Exit(-1);
                        }

                        Server = arg.Replace("stratum+", "").Replace("http://", "").Replace("tcp://", "").Split(':')[0].Replace("-o", "").Trim();

                        string PortNum = "";
                        try
                        {
                            PortNum = arg.Replace("http://", "").Replace("tcp://", "").Split(':')[1];
                            Port = Convert.ToInt16(PortNum);
                        }
                        catch
                        {
                            Console.WriteLine("Illegal port {0}", PortNum);
                            Environment.Exit(-1);
                        }
                    break;
                    
                    case "-u":
                        Username = arg.Replace("-u", "").Trim();
                    break;
                    
                    case "-p":
                        Password = arg.Replace("-p", "").Trim();
                    break;

                    case "-h":
                        break;

                    case "-c":
                        ComName = arg.Replace("-c", "").Trim();
                        break;

                    default:
                        Console.WriteLine("Illegal argument {0}", arg);
                        Environment.Exit(-1);
                    break;
                }
            }

            if (Server == "")
            {
                Console.WriteLine("Missing server URL. URL should be in the format of tcp://megahash.wemineltc.com:3333");
                Environment.Exit(-1);
            }
            else if (Port == 0)
            {
                Console.WriteLine("Missing server port. URL should be in the format of tcp://megahash.wemineltc.com:3333");
                Environment.Exit(-1);
            }
            else if (Username == "")
            {
                Console.WriteLine("Missing username");
                Environment.Exit(-1);
            }
            else if (Password == "")
            {
                Console.WriteLine("Missing password");
                Environment.Exit(-1);
            }

            Console.WriteLine("Connecting to '{0}' on port '{1}' with username '{2}' and password '{3}'. Fpga on serial '{4}'", Server, Port, Username, Password, ComName);
            Console.WriteLine();

            CoinMiner = new Miner(ComName);
            CoinValidator = new Validator();
            stratum = new Stratum();

            // Workaround for pools that keep disconnecting if no work is submitted in a certain time period. Send regular mining.authorize commands to keep the connection open
            KeepaliveTimer = new System.Timers.Timer(45000);
            KeepaliveTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            KeepaliveTimer.Start();

            // Set up event handlers
            stratum.GotResponse += stratum_GotResponse;
            stratum.GotSetDifficulty += stratum_GotSetDifficulty;
            stratum.GotNotify += stratum_GotNotify;

            // Connect to the server
            stratum.ConnectToServer(Server, Port, Username, Password);

            // Start mining!!
            StartCoinMiner();

            // This thread waits forever as the mining happens on other threads. Can press Ctrl+C to exit
            Thread.Sleep(System.Threading.Timeout.Infinite);
        }

        static void StartCoinMiner()
        {
            // Wait for a new job to appear in the queue
            while (IncomingJobs.Count == 0)
                Thread.Sleep(500);

            // Get the job
            Job ThisJob = IncomingJobs.Dequeue();

            if (ThisJob.CleanJobs)
                stratum.ExtraNonce2 = 0;

            // Increment ExtraNonce2
            stratum.ExtraNonce2++;

            // Calculate MerkleRoot and Target
            string MerkleRoot = Utilities.GenerateMerkleRoot(ThisJob.Coinb1, ThisJob.Coinb2, stratum.ExtraNonce1, stratum.ExtraNonce2.ToString("x8"), ThisJob.MerkleNumbers);
            string Target = Utilities.GenerateTarget(CurrentDifficulty);

            // Update the inputs on this job
            ThisJob.Target = Target;
            ThisJob.Data = ThisJob.Version + ThisJob.PreviousHash + MerkleRoot + ThisJob.NetworkTime + ThisJob.NetworkDifficulty;

            // Start a new miner in the background and pass it the job
            worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(CoinMiner.Mine);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CoinMinerCompleted);
            worker.RunWorkerAsync(ThisJob);

            validatorWorker = new BackgroundWorker();
            validatorWorker.DoWork += new DoWorkEventHandler(CoinValidator.Mine);
            validatorWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CoinValidatorCompleted);
            validatorWorker.RunWorkerAsync(ThisJob);
        }

        static void CoinValidatorCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // If the miner returned a result, submit it
            if (e.Result != null)
            {
                Job ThisJob = (Job)e.Result;
                Console.WriteLine("Validotor says: " + ThisJob.ToString());
            }
        }


        static void CoinMinerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // If the miner returned a result, submit it
            if (e.Result != null)
            {
                Job ThisJob = (Job)e.Result;
                SharesSubmitted++;

                stratum.SendSUBMIT(ThisJob.JobID, ThisJob.Data.Substring(68 * 2, 8), ThisJob.Answer.ToString("x8"), CurrentDifficulty);
            }

            // Mine again
            StartCoinMiner();
        }

        static void stratum_GotResponse(object sender, StratumEventArgs e)
        {
            StratumResponse Response = (StratumResponse)e.MiningEventArg;

            Console.Write("Got Response to {0} - " , (string)sender);

            switch ((string)sender)
            {
                case "mining.authorize":
                    if ((bool)Response.result)
                        Console.WriteLine("Worker authorized");
                    else
                    {
                        Console.WriteLine("Worker rejected");
                        Environment.Exit(-1);
                    }
                    break;

                case "mining.subscribe":
                    stratum.ExtraNonce1 = (string)((object[])Response.result)[1];
                    Console.WriteLine("Subscribed. ExtraNonce1 set to " + stratum.ExtraNonce1);
                    break;

                case "mining.submit":
                    if (Response.result != null && (bool)Response.result)
                    {
                        SharesAccepted++;
                        Console.WriteLine("Share accepted ({0} of {1})", SharesAccepted, SharesSubmitted);
                    }
                    else
                        Console.WriteLine("Share rejected. {0}", Response.error[1]);
                    break;
            }
        }

        static void stratum_GotSetDifficulty(object sender, StratumEventArgs e)
        {
            StratumCommand Command = (StratumCommand)e.MiningEventArg;
            CurrentDifficulty = Convert.ToInt32(Command.parameters[0]);

            Console.WriteLine("Got Set_Difficulty " + CurrentDifficulty);
        }

        static void stratum_GotNotify(object sender, StratumEventArgs e)
        {
            Job ThisJob = new Job();
            StratumCommand Command = (StratumCommand)e.MiningEventArg;

            ThisJob.JobID = (string)Command.parameters[0];
            ThisJob.PreviousHash = (string)Command.parameters[1];
            ThisJob.Coinb1 = (string)Command.parameters[2];
            ThisJob.Coinb2 = (string)Command.parameters[3];
            Array a = (Array)Command.parameters[4];
            ThisJob.Version = (string)Command.parameters[5];
            ThisJob.NetworkDifficulty = (string)Command.parameters[6];
            ThisJob.NetworkTime = (string)Command.parameters[7];
            ThisJob.CleanJobs = (bool)Command.parameters[8];

            ThisJob.MerkleNumbers = new string[a.Length];

            int i = 0;
            foreach (string s in a)
                ThisJob.MerkleNumbers[i++] = s;

            // Cancel the existing mining threads and clear the queue if CleanJobs = true
            if (ThisJob.CleanJobs)
            {
                Console.WriteLine("Stratum detected a new block. Stopping old threads.");
                IncomingJobs.Clear();
                CoinMiner.newBlock = true;
            }

            // Add the new job to the queue
            IncomingJobs.Enqueue(ThisJob);
        }
    }

    public class Job
    {
        // Inputs
        public string JobID;
        public string PreviousHash;
        public string Coinb1;
        public string Coinb2;
        public string[] MerkleNumbers;
        public string Version;
        public string NetworkDifficulty;
        public string NetworkTime;
        public bool CleanJobs;

        // Intermediate
        public string Target;
        public string Data;

        // Output
        public uint Answer;
    }
}


