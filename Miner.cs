using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.IO.Ports;

namespace DotNetStratumMiner
{
    class Miner
    {
        // General Variables
        public volatile bool done = false;
        public volatile uint FinalNonce = 0;
        public string portName;

        Thread[] threads;

        public Miner(string portName, int? optThreadCount = null)
        {
            int threadCount = optThreadCount ?? Environment.ProcessorCount;
            if (threadCount > Environment.ProcessorCount)
            {
                threadCount = Environment.ProcessorCount;
            }
            threads = new Thread[threadCount];
            this.portName = portName;
        }

        public uint SerialWriteRead(byte[] data, byte[] target)
        {
            SerialPort serialPort;
            serialPort = new SerialPort();
            serialPort.PortName = portName;
            serialPort.BaudRate = 115200;
            serialPort.Open();
            serialPort.WriteLine(String.Format("{0}{1}", data, target));
            uint nonce = UInt32.Parse(serialPort.ReadLine());
            serialPort.Close();
            return nonce;
        }

        public void Mine(object sender, DoWorkEventArgs e)
        {
            Debug.WriteLine("New Miner. ID = " + Thread.CurrentThread.ManagedThreadId);

            Job ThisJob = (Job)e.Argument;

            // Gets the data to hash and the target from the work
            byte[] databyte = Utilities.ReverseByteArrayByFours(Utilities.HexStringToByteArray(ThisJob.Data));
            byte[] targetbyte = Utilities.HexStringToByteArray(ThisJob.Target);

            done = false;
            FinalNonce = 0;

            FinalNonce = SerialWriteRead(databyte, targetbyte);

            if (FinalNonce != 0)
            {
                ThisJob.Answer = FinalNonce;
                e.Result = ThisJob;
            }
            else
                e.Result = null;

            Debug.WriteLine("Miner ID {0} finished", Thread.CurrentThread.ManagedThreadId);
        }

        // Reference: https://github.com/replicon/Replicon.Cryptography.SCrypt
        public void doScrypt(byte[] Tempdata, byte[] Target, uint Nonce, uint Increment)
        {
            double Hashcount = 0;

            byte[] Databyte = new byte[80];
            Array.Copy(Tempdata, 0, Databyte, 0, 76);

            Debug.WriteLine("New thread");

            DateTime StartTime = DateTime.Now;

            try
            {
                byte[] ScryptResult = new byte[32];

                // Loop until done is set or we meet the target
                while (!done)
                {
                    Databyte[76] = (byte)(Nonce >> 0);
                    Databyte[77] = (byte)(Nonce >> 8);
                    Databyte[78] = (byte)(Nonce >> 16);
                    Databyte[79] = (byte)(Nonce >> 24);

                    ScryptResult = Replicon.Cryptography.SCrypt.SCrypt.DeriveKey(Databyte, Databyte, 1024, 1, 1, 32);

                    Hashcount++;
                    if (meetsTarget(ScryptResult, Target))  // Did we meet the target?
                    {
                        if (!done)
                            FinalNonce = Nonce;
                        done = true;
                        break;
                    }
                    else
                        Nonce += Increment; // If not, increment the nonce and try again
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                FinalNonce = 0;
            }

            double Elapsedtime = (DateTime.Now - StartTime).TotalMilliseconds;
            Console.WriteLine("Thread finished - {0:0} hashes in {1:0.00} ms. Speed: {2:0.00} kHash/s", Hashcount, Elapsedtime, Hashcount / Elapsedtime);
        }

        public bool meetsTarget(byte[] hash, byte[] target)
        {
            for (int i = hash.Length - 1; i >= 0; i--)
            {
                if ((hash[i] & 0xff) > (target[i] & 0xff))
                    return false;
                if ((hash[i] & 0xff) < (target[i] & 0xff))
                    return true;
            }
            return false;
        }
    }

}
