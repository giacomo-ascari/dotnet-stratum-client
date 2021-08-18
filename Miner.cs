using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.IO.Ports;
using System.Text;

namespace DotNetStratumMiner
{
    class Miner
    {
        private bool _wait=true;
        private byte[] ScryptResult = new byte[32];
        // General Variables
        public volatile bool done = false;
        public volatile uint FinalNonce = 0;
        public string portName;

        public Miner(string portName)
        {
            this.portName = portName;
        }

        public void SerialWriteRead(string data, byte[] target)
        {
            byte[] ScryptResult = new byte[32];
            SerialPort serialPort;
            serialPort = new SerialPort(portName)
            {
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None
            };
            serialPort.DataReceived += new SerialDataReceivedEventHandler(readData);
            serialPort.ErrorReceived += SerialPort_ErrorReceived;
            serialPort.Open();
            Thread.Sleep(1);
            serialPort.WriteLine(String.Format("{0}", data));
            Console.WriteLine("prova");
            while (_wait) ;
            _wait = true;
            serialPort.Close();
            return;
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Console.WriteLine(e.EventType);
        }

        private void readData(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort port = (SerialPort)sender;
            string data = port.ReadLine();
            if (data.Length == 64)
            {
                ScryptResult = Encoding.ASCII.GetBytes(data);
                Console.WriteLine($"DATA RECEIVED: {data}");
                _wait = false;
            }
        }

        public void Mine(object sender, DoWorkEventArgs e)
        {

            Job ThisJob = (Job)e.Argument;
            Console.WriteLine("New Miner");
            Console.WriteLine("Data: {0}\nTarget: {1}", ThisJob.Data, ThisJob.Target);

            // Gets the data to hash and the target from the work
            byte[] databyte = Utilities.ReverseByteArrayByFours(Utilities.HexStringToByteArray(ThisJob.Data));
            byte[] targetbyte = Utilities.HexStringToByteArray(ThisJob.Target);

            done = false;
            FinalNonce = 0;
            double Hashcount = 0;
            uint Nonce = 0;
            byte[] Databyte = new byte[80];
            //byte[] ScryptResult = new byte[32];
            while (!done)
            {
                Databyte[76] = (byte)(Nonce >> 0);
                Databyte[77] = (byte)(Nonce >> 8);
                Databyte[78] = (byte)(Nonce >> 16);
                Databyte[79] = (byte)(Nonce >> 24);

                SerialWriteRead(ThisJob.Data, targetbyte);
                Console.Write(".");
                Hashcount++;
                if (meetsTarget(ScryptResult, targetbyte))
                {
                    if (!done)
                        FinalNonce = Nonce;
                    done = true;
                    break;
                }
                else
                {
                    Nonce++;
                }
                    
            }

            if (FinalNonce != 0)
            {
                ThisJob.Answer = FinalNonce;
                e.Result = ThisJob;
            }
            else
            {
                e.Result = null;
            }

            Console.WriteLine("FinalNonce: {0}", FinalNonce);
            Console.WriteLine("Miner finished");
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
