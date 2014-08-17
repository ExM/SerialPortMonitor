using Fclp;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialPortMonitor
{
	class Program
	{
		private static Logger _log = LogManager.GetCurrentClassLogger();

		private static ManualResetEvent _sync = new ManualResetEvent(false);

		private static volatile bool _redirectToConsole = false;
		private static Stopwatch _sw = new Stopwatch();
		private static TimeSpan _lastTime = TimeSpan.Zero;

		static int Main(string[] args)
		{
			string portName = null;
			int baudrate = 9600;

			var p = new FluentCommandLineParser();
			
			p.Setup<string>('p', "port")
				.Callback(_ => portName = _)
				.WithDescription("serial port name")
				.Required();

			p.Setup<int>('b', "baudrate")
				.Callback(_ => baudrate = _)
				.WithDescription("the baud rate");

			p.Setup<bool>('c', "redirectToConsole")
				.Callback(_ => _redirectToConsole = _)
				.WithDescription("redirect to console received data");

			p.SetupHelp("?", "h", "help")
				.Callback(_ => Console.WriteLine(_))
				.WithHeader("SerialPortMonitor - show reseived data from serial port");
			
			var result = p.Parse(args);

			if (result.HelpCalled)
				return 0;

			if (result.HasErrors)
			{
				Console.WriteLine("Error comman line parameters: {0}", result.ErrorText);
				return 1;
			}
			
			Console.CancelKeyPress += OnCtrlC;
			try
			{
				using (SerialPort port = new SerialPort(portName))
				{
					port.BaudRate = baudrate;
					port.DataReceived += OnDataReceived;
					_sw.Start();
					port.Open();
					
					_log.Debug("{0} port opened. Baud rate: {1}", port.PortName, port.BaudRate);
					_sync.WaitOne();
				}

				LogManager.Flush();
				return 0;
			}
			catch(Exception ex)
			{
				Console.WriteLine("Error: ");

				while (ex != null)
				{
					Console.WriteLine("   {0}", ex.Message);
					ex = ex.InnerException;
				}

				LogManager.Flush();
				return 1;
			}
		}

		static void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			var current = _sw.Elapsed;

			if(e.EventType == SerialData.Eof)
			{
				_sync.Set();
				return;
			}

			SerialPort port = (SerialPort)sender;

			byte[] buffer = new byte[port.BytesToRead];

			port.Read(buffer, 0, buffer.Length);

			var asciiText = Encoding.ASCII.GetString(buffer);
			var hexText = string.Join(" ", buffer.Select(_ => _.ToString("x2")));

			var logInfo = new LogEventInfo(LogLevel.Info, _log.Name, "Received " + buffer.Length + " bytes");
			logInfo.Properties["hex"] = hexText;
			logInfo.Properties["ascii"] = asciiText;
			logInfo.Properties["port"] = port.PortName;
			logInfo.Properties["time"] = current;
			logInfo.Properties["delta"] = current - _lastTime;
			_log.Log(logInfo);
			if (_redirectToConsole)
				Console.Write(asciiText);
			_lastTime = current;
		}

		private static void OnCtrlC(object sender, ConsoleCancelEventArgs args)
		{
			Console.WriteLine("\nThe read operation has been interrupted.");
			args.Cancel = true;
			_sync.Set();
		}
	}
}
