/*=========================
 * Serial Comm Class
 *  Define serial comm with simplified interface
 * Versions
 *  1.0 Initial Version
 *  1.1 Fix naming, add message on write exception
 *  1.2 Allowed read callback to be null if only send is required.
 *  1.3 Changed ReceivedData from delegate to event
=========================*/

using System;
using System.IO.Ports;
using System.Text;
using Microsoft.SPOT;

namespace Samraksh.Components.Utility
{
	/// <summary>
	/// Sends and receives from a serial port.
	/// </summary>
	public class SerialComm
	{
		/// <summary>Serial port name</summary>
		public SerialPort Port { get; private set; }

		/// <summary>Delegate for read callback</summary>
		/// <param name="readBytes">Bytes read</param>
		public delegate void ReceiveCallback(byte[] readBytes);

		/// <summary>Client callback (can be null)</summary>
		public event ReceiveCallback DataReceived;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="serialPortName">Serial port name (e.g. COM1)</param>
		/// <param name="receiveCallback">Optional callback method to process incoming data.</param>
		public SerialComm(string serialPortName, ReceiveCallback receiveCallback = null)
		{
			if (receiveCallback != null)
			{
				DataReceived += receiveCallback;
			}
			// Set up the serial port
			Port = new SerialPort(serialPortName, 115200, Parity.None, 8, StopBits.One) { Handshake = Handshake.None };
			Port.DataReceived += PortHandler;
		}

		/// <summary>
		/// Open the port
		/// </summary>
		public void Open()
		{
			Port.Open();
		}

		private readonly char[] _oneCharArray = new char[1];
		/// <summary>
		/// Write a single char
		/// </summary>
		/// <param name="theChar"></param>
		/// <returns></returns>
		public bool Write(char theChar)
		{
			_oneCharArray[0] = theChar;
			return Write(_oneCharArray);
		}

		/// <summary>
		/// Write a char array to the port
		/// </summary>
		/// <param name="theChars"></param>
		/// <returns></returns>
		public bool Write(char[] theChars)
		{
			try
			{
				var bytes = new byte[theChars.Length];
				for (var i = 0; i < theChars.Length; i++)
				{
					bytes[i] = (byte)theChars[i];
				}
				//var bytes = Encoding.UTF8.GetBytes(new string(theChars));
				Port.Write(bytes, 0, bytes.Length);
				Port.Flush();
				return true;
			}
			catch (Exception ex)
			{
				Debug.Print("SerialComm Write(char) exception " + ex);
				return false;

			}
		}

		/// <summary>
		/// Write a string to the port
		/// </summary>
		/// <param name="str">The string to write</param>
		/// <remarks>Flushes the port after writing the bytes to ensure it all gets sent.</remarks>
		/// <returns></returns>
		public bool Write(string str)
		{
			// Lock the write to avoid possible race conditions
			lock (_serialCommLock)
			{
				try
				{
					var bytes = Encoding.UTF8.GetBytes(str);
					Port.Write(bytes, 0, bytes.Length);
					Port.Flush();
					return true;
				}
				catch (Exception ex)
				{
					Debug.Print("SerialComm Write(String) exception " + ex);
					return false;
				}
			}
		}

        public bool Write(byte[] bytes)
        {
            // Lock the write to avoid possible race conditions
            lock (_serialCommLock)
            {
                try
                {
                    Port.Write(bytes, 0, bytes.Length);
                    Port.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Print("SerialComm Write(String) exception " + ex);
                    return false;
                }
            }
        }

		private object _serialCommLock = new object();

		/// <summary>
		/// Handle a read event
		/// </summary>
		/// <remarks>Reads the incoming data and calls a user-provided method to process it.</remarks>
		/// <param name="sender">The sender</param>
		/// <param name="e">The event args</param>
		private void PortHandler(object sender, SerialDataReceivedEventArgs e)
		{
			// If no read callback specified, ignore whatever came in
			if (DataReceived == null)
			{
				return;
			}
			var numBytes = Port.BytesToRead;
			var recvBuffer = new byte[numBytes];
			Port.Read(recvBuffer, 0, numBytes);
			DataReceived(recvBuffer);
		}
	}
}
