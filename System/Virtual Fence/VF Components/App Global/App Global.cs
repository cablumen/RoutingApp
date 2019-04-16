//#define DBG_VERBOSE
//#define DBG_SIMPLE
#define DBG_LOGIC

// Ensure exactly one defined
#if !DBG_VERBOSE && !DBG_SIMPLE && !DBG_LOGIC
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif
#if  DBG_VERBOSE && (DBG_SIMPLE || DBG_LOGIC) || DBG_SIMPLE && (DBG_VERBOSE || DBG_LOGIC) || DBG_LOGIC && (DBG_VERBOSE || DBG_SIMPLE)
#error Exactly one of DBG_VERBOSE, DBG_SIMPLE, DBG_LOGIC must be defined.
#endif

using System;
using System.Diagnostics;
using System.Text;

using Samraksh.eMote.Net;
using Samraksh.eMote.Net.MAC;

#if !PC
using BitConverter = Samraksh.Components.Utility.BitConverter;
using Math = System.Math;
#endif

namespace Samraksh.VirtualFence.Components
{
	/// <summary>
	/// Common items for all system nodes
	/// </summary>
	public static class AppGlobal
	{
        /// <summary>
        /// Size of detection message
        /// </summary>
        public const int DetectionMessageSize = sizeof(byte) + sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte); // Additional field for TTL
        public static ushort TempParent;

#if !PC
		/// <summary>Application pipe</summary>
		public static MACPipe AppPipe { get; set; }
#endif

		/// <summary>
		/// App message IDs
		/// </summary>
		public enum MessageIds : byte
		{
			/// <summary>Detection has occurred</summary>
			Detect = 0,
            Send = 1,
            Recieve = 2,
		}

        public static ushort SendToTempParent(MACPipe mac, byte[] msgBytes, int length)
        {
            return mac.EnqueueToSend(TempParent, msgBytes, 0, (ushort)length);
        }

		/// <summary>
		/// Classification results
		/// </summary>
		/// <remarks>
		///		Values should not include *, [, ] or control chars.
		///		These are converted to chars and sent to PC as part of an ASCII string
		/// </remarks>
		public enum ClassificationType : byte
		{
			/// <summary>Detection, no classification yet</summary>
			Detect = (byte)'D',
            /// <summary>TCP, no classification yet</summary>
            Send = (byte)'S',
            /// <summary>UDP, no classification yet</summary>
            Recieve = (byte)'R',
			/// <summary>Provisional Human</summary>
			ProvisionalHuman = (byte)'h',
			/// <summary>Provisional Non-human</summary>
			ProvisionalNonHuman = (byte)'n',
			/// <summary>Provisional ambiguous</summary>
			ProvisionalAmbiguous = (byte)'a',

			/// <summary>Final Human</summary>
			FinalHuman = (byte)'H',
			/// <summary>Final Non-human</summary>
			FinalNonHuman = (byte)'N',
			/// <summary>Final ambiguous</summary>
			FinalAmbiguous = (byte)'A',
		}

#if !PC
		//============================================================================================================================
		/// <summary>
		/// Mote Messages
		/// </summary>
		public static class MoteMessages
		{
			/// <summary>
			/// Compose App messages
			/// </summary>
			public static class Compose
			{
				/// <summary>
				/// Compose a detection message
				/// </summary>
				/// <param name="msgBytes"></param>
				/// <param name="originator"></param>
				/// <param name="classificatonType"></param>
				/// <param name="detectionNum"></param>
				/// <returns></returns>
				public static int Detection(byte[] msgBytes, ushort originator, ClassificationType classificatonType,
					int detectionNum, byte TTL)
				{
					var idx = 0;

					// Detection message type
					msgBytes[idx] = (byte)MessageIds.Detect;
					idx++;

					// Classification
					msgBytes[idx] = (byte)classificatonType;
					idx++;

					// Detection message number
					var detectNum = (ushort)Math.Min(detectionNum, ushort.MaxValue);
					BitConverter.InsertValueIntoArray(msgBytes, idx, detectNum);
					idx += sizeof(ushort);

					// Message originator
					BitConverter.InsertValueIntoArray(msgBytes, idx, originator);
					idx += sizeof(ushort);

                    // TTL
                    msgBytes[idx] = (byte)TTL;
                    idx++;

					// Check if message is the right size
					if (idx != DetectionMessageSize)
					{
						throw new Exception("Compose Detection: message composed is " + idx + " bytes; should be " + DetectionMessageSize);
					}

					return idx;
				}
                public static int SendPacket(byte[] msgBytes, ushort originator, ClassificationType classificatonType, int sndNumber, byte TTL, int pathLength, ushort[] path)
                {
                    var idx = 0;

                    // Detection message type
                    msgBytes[idx] = (byte)MessageIds.Send;
                    idx++;

                    // Classification
                    msgBytes[idx] = (byte)classificatonType;
                    idx++;

                    // Detection message number
                    var TCPNum = (ushort)Math.Min(sndNumber, ushort.MaxValue);
                    BitConverter.InsertValueIntoArray(msgBytes, idx, TCPNum);
                    idx += sizeof(ushort);

                    // Message originator
                    BitConverter.InsertValueIntoArray(msgBytes, idx, originator);
                    idx += sizeof(ushort);

                    // TTL
                    msgBytes[idx] = (byte)TTL;
                    idx++;

                    // Path Length
                    BitConverter.InsertValueIntoArray(msgBytes, idx, pathLength);
                    idx += sizeof(ushort);

                    // Add Path
                    int i;
                    for (i = 0; i < pathLength; i = i + 1)
                    {

                        BitConverter.InsertValueIntoArray(msgBytes, idx, path[i]);
                        idx += sizeof(ushort);
                    }
                    return idx;
                }
                public static int RecievePacket(byte[] msgBytes, ushort originator, ClassificationType classificatonType, int UDPNumber, byte TTL, int pathLength, ushort[] path)
                {
                    var idx = 0;

                    // Detection message type
                    msgBytes[idx] = (byte)MessageIds.Recieve;
                    idx++;

                    // Classification
                    msgBytes[idx] = (byte)classificatonType;
                    idx++;

                    // Detection message number
                    var UDPNum = (ushort)Math.Min(UDPNumber, ushort.MaxValue);
                    BitConverter.InsertValueIntoArray(msgBytes, idx, UDPNum);
                    idx += sizeof(ushort);

                    // Message originator
                    BitConverter.InsertValueIntoArray(msgBytes, idx, originator);
                    idx += sizeof(ushort);

                    // TTL
                    msgBytes[idx] = (byte)TTL;
                    idx++;

                    // Path Length
                    BitConverter.InsertValueIntoArray(msgBytes, idx, pathLength);
                    idx += sizeof(ushort);

                    // Add Path
                    int i;
                    for (i = 0; i < pathLength; i = i + 1)
                    {

                        BitConverter.InsertValueIntoArray(msgBytes, idx, path[i]);
                        idx += sizeof(ushort);
                    }
                    return idx;
                }
			}

			/// <summary>
			/// Parse App messages received
			/// </summary>
			public static class Parse
			{
				/// <summary>
				/// Parse detection message
				/// </summary>
				/// <param name="msgBytes"></param>
				/// <param name="classificationType"></param>
				/// <param name="detectionNum"></param>
				/// <param name="originator"></param>
				public static void Detection(byte[] msgBytes, out ClassificationType classificationType,
					out ushort detectionNum, out ushort originator, out byte TTL)
				{
					var idx = 1; // Start at 1 since we've already checked the message ID

					classificationType = (ClassificationType)msgBytes[idx];
					idx += 1;

					detectionNum = BitConverter.ToUInt16(msgBytes, idx);
					idx += sizeof(ushort);

					originator = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    TTL = msgBytes[idx];
                    idx += 1;
				}
                public static ushort[] SendPacket(byte[] msgBytes, out ClassificationType classificationType, 
                    out ushort TCPNumber, out ushort originator, out byte TTL, out ushort pathLength)
                {
                    var idx = 1; // Start at 1 since we've already checked the message ID

                    classificationType = (ClassificationType)msgBytes[idx];
                    idx += 1;

                    TCPNumber = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    originator = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    TTL = msgBytes[idx];
                    idx += 1;
                    
                    pathLength = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    ushort[] path = new ushort[pathLength];
                    int i;
                    for(i = 0; i < pathLength; i = i + 1){
                        path[i] = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }
                    return path;
                }
                public static ushort[] RecievePacket(byte[] msgBytes, out ClassificationType classificationType,
                    out ushort UDPNumber, out ushort originator, out byte TTL, out ushort pathLength, out ushort cur_node)
                {
                    var idx = 1; // Start at 1 since we've already checked the message ID

                    classificationType = (ClassificationType)msgBytes[idx];
                    idx += 1;

                    UDPNumber = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    originator = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    TTL = msgBytes[idx];
                    idx += 1;

                    pathLength = BitConverter.ToUInt16(msgBytes, idx);
                    idx += sizeof(ushort);

                    ushort[] path_minus_self = new ushort[pathLength - 1];
                    int i;
                    for (i = 0; i < pathLength - 1; i = i + 1)
                    {
                        path_minus_self[i] = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }
                    if (pathLength > 1)
                    {
                        cur_node = BitConverter.ToUInt16(msgBytes, idx);
                        idx += sizeof(ushort);
                    }
                    else
                    {
                        cur_node = 0;
                    }
                    return path_minus_self;
                }
			}
		}
#endif

		//============================================================================================================================
		/// <summary>
		/// PC Messages
		/// </summary>
		public static class PCMessages
		{
			/// <summary>
			/// Compose messages to send to PC
			/// </summary>
			public static class Compose
			{
				/// <summary>
				/// Compose Detection message to send to PC
				/// </summary>
				/// <param name="originator"></param>
				/// <param name="classificatonType"></param>
				/// <param name="detectionNumber"></param>
				/// <returns></returns>
				/// <remarks>Bracket message with * ... \n</remarks>
				public static string Detection(int originator, ClassificationType classificatonType, int detectionNumber)
				{
					var msgS = SystemGlobal.PCMessages.MsgHeader(SystemGlobal.PCMessages.PCMacPipeIds.App);
					msgS.Append((int)MessageIds.Detect);

					msgS.Append(' ');
					msgS.Append(originator);

					msgS.Append(' ');
					msgS.Append((int)classificatonType);

					msgS.Append(' ');
					msgS.Append(detectionNumber);

					SystemGlobal.PCMessages.MsgTrailer(msgS);
					//msgS.Append('\n');
					return msgS.ToString();
				}
			}
#if PC
			/// <summary>
			/// Parse messages received by PC
			/// </summary>
			public static class Parse
			{
				/// <summary>
				/// Parse Detection message from mote
				/// </summary>
				/// <param name="args"></param>
				/// <param name="originator"></param>
				/// <param name="classificationType"></param>
				/// <param name="detectionNum"></param>
				/// <remarks>Assumes that bracketing * (payload_type) ... \n have been removed</remarks>
				public static void Detection(string[] args, out ushort originator, out ClassificationType classificationType, out ushort detectionNum)
				{
					const int firstArg = 2;
					const int numArgs = 3;
					var argNo = 0;

					if (args.Length - firstArg != numArgs)
					{
						throw new Exception(string.Format("Invalid number of arguments for Detection message. S/b {0}, found {1}\n{2}", numArgs, args.Length - firstArg, string.Join(" ", args)));
					}

					// Get originator
					if (!ushort.TryParse(args[firstArg + argNo], out originator))
					{
						throw new Exception(string.Format("Detection message: Error converting originator {0}", args[firstArg + argNo]));
					}
					argNo++;

					// Get classification type
					byte classificationTypeB;
					if (!byte.TryParse(args[firstArg + argNo], out classificationTypeB))
					{
						throw new Exception(string.Format("Classification message: Error converting classification type {0}", args[firstArg + argNo]));
					}
					classificationType= (ClassificationType)classificationTypeB;
					argNo++;

					// Get detection number
					if (!ushort.TryParse(args[firstArg + argNo], out detectionNum))
					{
						throw new Exception(string.Format("Detection message: Error converting detection number {0}", args[firstArg + argNo]));
					}
					argNo++;

					
					if (argNo != numArgs)
					{
						throw new Exception(string.Format("Detection message: Number of arguments parsed: {0}; s/b {1}", numArgs, argNo));
					}

				}
			}
#endif
		}

	}
}



