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
        private SerialPort serialPort;
        private byte[] ScryptResult = new byte[32];
        // General Variables
        public volatile bool done = false, newBlock;
        public volatile uint FinalNonce = 0;
        public string portName;

        public Miner(string portName)
        {
            this.portName = portName;
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
        }

        public void SerialWriteRead(string data, string target)
        {
            //Thread.Sleep(1);
            //data = "0100000081cd02ab7e569e8bcd9317e2fe99f2de44d49ab2b8851ba4a308000000000000e320b6c2fffc8d750423db8b1eb942ae710e951ed797f7affc8892b0f1fc122bc7f5d74df2b9441a";
            //target = "000000000000000000000000000000000000000000000000000000000000000f";
            //serialPort.Write(data);
            //serialPort.Write(target);
            serialPort.Write(String.Format("<DTA>{0}\n<TRG>{1}\n", data, target));
            Console.Write(String.Format("<DTA>{0}\n<TRG>{1}\n", data, target));
            Console.WriteLine(serialPort.BytesToWrite);
            //Console.WriteLine(String.Format("data: {0}\ntarget: {1}", data, target));
            Console.WriteLine($"Started: {DateTime.Now}");
            //Console.WriteLine(String.Format("{0}", data));
            //Console.WriteLine("prova");
            //newBlock = true;
            while (!done)
            {
                if (newBlock)
                {
                    serialPort.Write("<NBF>\n");
                    while (newBlock) ; //waits for the fpga to be acknowledged
                    //Console.WriteLine("ack");
                    break;
                }
            }
            done = false;
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
            Console.Write(data+ " . ");
            //Console.WriteLine("data: "+data);
            //data = "9c63289869bb8999cac6a36d93813610b5041d568021249aa1b0b3e3ed4ff88d";
            if (data.StartsWith("<NNC>"))
            {
                Console.WriteLine($"Completed: {DateTime.Now}");
                //uint num = uint.Parse(data, System.Globalization.NumberStyles.AllowHexSpecifier);
                //ScryptResult = BitConverter.GetBytes(num);
                //Console.WriteLine("nonce: " + data.Substring(7, 8));
                FinalNonce = uint.Parse(data.Substring(5, 8), System.Globalization.NumberStyles.HexNumber);
                Console.WriteLine($"DATA RECEIVED: {data}");
                //Console.WriteLine(FinalNonce);
                done = true;
            }
            else if(data == "<ACK>")
            {
                Console.WriteLine("ack received");
                newBlock = false;
            }
        }

        public void Mine(object sender, DoWorkEventArgs e)
        {
            Job ThisJob = (Job)e.Argument;
            Console.WriteLine("New Miner");
            Console.WriteLine("Data: {0}\nTarget: {1}", ThisJob.Data, ThisJob.Target);

            // Gets the data to hash and the target from the work
            //byte[] databyte = Utilities.ReverseByteArrayByFours(Utilities.HexStringToByteArray(ThisJob.Data));
            //byte[] targetbyte = Utilities.HexStringToByteArray(ThisJob.Target);

            done = false;
            newBlock = false;
            FinalNonce = 0;
            //double Hashcount = 0;
            //uint Nonce = 0;
            //byte[] Databyte = new byte[80];
            //byte[] ScryptResult = new byte[32];
            //while (!done)
            //{
                //databyte.CopyTo(Databyte, 0);
                //BitConverter.GetBytes(Nonce).CopyTo(Databyte, Databyte.Length-4);
                //Databyte[76] = (byte)(Nonce >> 0);
                //Databyte[77] = (byte)(Nonce >> 8);
                //Databyte[78] = (byte)(Nonce >> 16);
                //Databyte[79] = (byte)(Nonce >> 24);


                if (!serialPort.IsOpen)
                {
                    serialPort.Open();
                }
                SerialWriteRead(ThisJob.Data, ThisJob.Target);
                //done = true;
                //Console.Write(".");
                //Hashcount++;
                //if (meetsTarget(ScryptResult, targetbyte))
                //{
                //    if (!done)
                //        FinalNonce = Nonce;
                //    done = true;
                //    break;
                //}
                //else
                //{
                //    Nonce++;
                //}
                    
            //}

            if (FinalNonce != 0)
            {
                ThisJob.Answer = FinalNonce;
                e.Result = ThisJob;
            }
            else
            {
                e.Result = null;
            }

            serialPort.Close();
            Console.WriteLine("FinalNonce: {0}", FinalNonce);
            Console.WriteLine("Miner finished");
        }

        public bool meetsTarget(byte[] hash, byte[] target)
        {
            Console.WriteLine(hash.Length);
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
