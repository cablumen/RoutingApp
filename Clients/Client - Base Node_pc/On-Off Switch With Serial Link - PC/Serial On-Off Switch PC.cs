/*--------------------------------------------------------------------
See Program.cs for release info.
---------------------------------------------------------------------*/


using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using System.Collections;

namespace Serial_On_Off_Switch_PC {

	/// <summary>
	/// This PC client program interacts with an eMote .NOW server via the serial port.
	/// Communication is bi-directional:
	///      The mote server sends messages about switch state to the PC client, which are displayed in a text box.
	///      The user can make the PC client turn the message transmission on or off.
	/// </summary>
	public partial class SerialOnOffPc : Form {

		private const int MaxSerialPortNumber = 8;

		private SerialComm _serialComm;   // The serial comm object
		private bool _serialStarted;     // True iff serial port has been opened and thread started
		private bool _moteSwitchEnabled = true;  // True iff the mote switch is enabled 
        private int f_n=0;
        private Hashtable mockServer;

		/// <summary>
		/// Initializer
		/// </summary>
		public SerialOnOffPc() {
            mockServer= new Hashtable();
            mockServer.Add(0,9);
            mockServer.Add(1,8);
            mockServer.Add(2,7);
            mockServer.Add(3,6);
            mockServer.Add(4,5);
            mockServer.Add(5,4);
            mockServer.Add(6,3);
            mockServer.Add(7,2);
            mockServer.Add(8,1);
            mockServer.Add(9,0);

			InitializeComponent();
		}

		/// <summary>
		/// Load the form
		/// </summary>
		private void SerialOnOffPC_Load(object sender, EventArgs e) {
			// Get the list of serial ports
			RefreshSerialPortList_Click(new object(), new EventArgs());
		}

		/// <summary>
		/// A call-back method that's called by SerialComm whenever serial data has been received from the mote
		/// </summary>
		/// <param name="input">The data received</param>
        /// 
        
		private void ProcessInput(string input) {
			// We have to use a method invoker to avoid cross-thread issues
            //Console.WriteLine(f_n);
            if (f_n == 7)
            {
                f_n = 0;
                
                String tempString = " From mote got: " + input+"\n";
                Console.WriteLine(tempString);
                MethodInvoker m1 = () =>
                {
                    // Append the received data to the textbox
                    //Console.Write("inside lambda");
                    FromMote.AppendText(tempString);
                };
                if (FromMote.InvokeRequired)
                {
                    FromMote.Invoke(m1);
                }
                else
                {
                    m1();
                }
                int num = int.Parse(input);
                int numToReturn=0;
                if (mockServer.ContainsKey(input))
                    numToReturn = (int)mockServer[num];
                String tempStringReturn = " Sent Back " +  numToReturn + "\n";
                Console.WriteLine(tempStringReturn);
                MethodInvoker m2 = () =>
                {
                    // Append the received data to the textbox
                    //Console.Write("inside lambda");
                    FromMote.AppendText(tempStringReturn);
                };
                if (FromMote.InvokeRequired)
                {
                    FromMote.Invoke(m2);
                }
                else
                {
                    m2();
                }
                _serialComm.Write("fffffff" + numToReturn);

            }
            else if (input.Equals("f"))
            {
                f_n++;
            }
            return;
            if (true)
            {
                //Console.Write("decent input " + input +" with length  "+ input.Length+"\n");
            }
			MethodInvoker m = () => {
				// Append the received data to the textbox
                
				FromMote.AppendText(input);
			};
            //if (input.Length < 2)
            //    return;
			if (FromMote.InvokeRequired) {
				FromMote.Invoke(m);
			}
			else {
				m();
			}
		}
       
        

		/// <summary>
		/// Start or stop the serial comm
		/// </summary>
		private void StartStop_Click(object sender, EventArgs e) {
			StartStop.Enabled = false;
			// If serial started, stop it
			if (_serialStarted) {
				// Note that stopped
				_serialStarted = false;
				// Stop serial
				if (_serialComm != null) {
					_serialComm.Stop();
				}
				// Change control
				StartStop.Text = "Click to Enable Serial";
				StartStop.BackColor = Color.LightCoral;
			}
			// If serial stopped, start it
			else {
				var selectedComPort = SerialPortList.SelectedItem.ToString().ToUpper();
				var comPortNumberStr = selectedComPort.Replace("COM", string.Empty);
				int comPortNumber;
				var retVal = int.TryParse(comPortNumberStr, out comPortNumber);
                Console.Write("console is " + comPortNumberStr);
                /**
				if (!retVal) {
					MessageBox.Show(string.Format("Cannot get port number from selected item {0}", selectedComPort), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}
				if (comPortNumber > MaxSerialPortNumber) {
					MessageBox.Show(string.Format("Port numbers above {0} probably can't be opened", MaxSerialPortNumber), "Warning", MessageBoxButtons.OK,
						MessageBoxIcon.Warning);
				}
                 */
				var portName = selectedComPort;
				_serialComm = new SerialComm(portName, ProcessInput);
				// Try to start. If cannot open, give error message.
				if (!_serialComm.Start()) {
					ErrorMessages.AppendText("Cannot open serial port " + portName + "\n");
					StartStop.Enabled = true;
					return;
				}
				// Note that started and change control
				_serialStarted = true;
				StartStop.Text = "Click to Disable Serial";
				StartStop.BackColor = Color.YellowGreen;
			}
			StartStop.Enabled = true;
		}

		/// <summary>
		/// Enable or disable mote switch
		/// </summary>
		/// <remarks>
		/// This sends messages to the mote instructing it to send or not send switch input
		/// </remarks>
		private void EnableDisableMoteSwitch_Click(object sender, EventArgs e) {
			if (!_serialStarted || _serialComm == null) {
				return;
			}
			EnableDisableMoteSwitch.Enabled = false;
			// If mote switch is enabled then disable (send "0") and change control
            Random random = new Random();
            int numToSend = random.Next(0, 9);
			if (_moteSwitchEnabled) {
				_moteSwitchEnabled = false;
				_serialComm.Write("fffffff"+numToSend);
                Console.WriteLine("Sent to mote: "+ numToSend);
                MethodInvoker m1 = () =>
                {
                    // Append the received data to the textbox

                    FromMote.AppendText("Sent to mote: " + numToSend);
                };
                if (FromMote.InvokeRequired)
                {
                    FromMote.Invoke(m1);
                }
                else
                {
                    m1();
                }
				EnableDisableMoteSwitch.Text = "Click to Send Random Number";
                
				EnableDisableMoteSwitch.BackColor = Color.LightCoral;
			}
			// if mote switch is disabled then enable (send "1") and change control
			else {
				_moteSwitchEnabled = true;
                _serialComm.Write("fffffff" + numToSend);
                Console.WriteLine("Sent to mote: " + numToSend);
                MethodInvoker m1 = () =>
                {
                    // Append the received data to the textbox

                    FromMote.AppendText("Sent to mote: " + numToSend);
                };
                if (FromMote.InvokeRequired)
                {
                    FromMote.Invoke(m1);
                }
                else
                {
                    m1();
                }
				EnableDisableMoteSwitch.Text = "Click to Disable Mote Switch";
				EnableDisableMoteSwitch.BackColor = Color.YellowGreen;
			}
			EnableDisableMoteSwitch.Enabled = true;
		}

		/// <summary>
		/// Clear the messages received from the mote
		/// </summary>
		private void ClearFromMote_Click(object sender, EventArgs e) {
			FromMote.Clear();
		}

		/// <summary>
		/// Clear the any error messages
		/// </summary>
		private void ClearErrorMessages_Click(object sender, EventArgs e) {
			ErrorMessages.Clear();
		}

		/// <summary>
		/// Refresh the list of serial port names
		/// </summary>
		private void RefreshSerialPortList_Click(object sender, EventArgs e) {
			SerialPortList.Text = string.Empty;
			SerialPortList.DataSource = SerialPort.GetPortNames();
		}
	}
}
