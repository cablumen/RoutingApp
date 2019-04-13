//	Enable this symbol to let the class show the current state value on the eMote LCD
//		The alternative constructor with an argument for lcdPos should be used
//#define DebugViaLcd

//#define DebugViaPrint

#if DebugViaPrint
using Microsoft.SPOT;
#endif

using System;
using System.Threading;
using Microsoft.SPOT.Hardware;


namespace Samraksh.Components.Utility
{
	/// <summary>
	/// Debounce On-Off Switch
	/// </summary>
	public class DebouncedOnOffSwitch
	{
#if DebugViaLcd
		private readonly EnhancedEmoteLCD _lcd;
		private const int LcdPos = 2;
#endif

		private readonly int _onVal;

		/// <summary>Interrupt port instance</summary>
		public InterruptPort Port { get; private set; }

		/// <summary>Callback for short press (less than LongPressTime)</summary>
		public event NativeEventHandler OnShortPress;

		/// <summary>Callback for long press (at least LongPressTime)</summary>
		public event NativeEventHandler OnLongPress;

#if DebugViaLcd
		/// <summary>
		/// Constructor for debugging state machine
		/// </summary>
		/// <param name="thePin"></param>
		/// <param name="resistorMode"></param>
		/// <param name="onVal"></param>
		/// <param name="lcd"></param>
		public DebouncedOnOffSwitch(Cpu.Pin thePin, Port.ResistorMode resistorMode, bool onVal, EnhancedEmoteLCD lcd)
			: this(thePin, resistorMode, onVal)
		{
			_lcd = lcd;
			ShowStateOnLcd();
		}

		private void ShowStateOnLcd()
		{
			var lcdState = DebounceState.ToString().ToCharArray()[0].ToLcd();
			_lcd.WriteN(LcdPos, lcdState);
		}
#endif

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="thePin"></param>
		/// <param name="resistorMode"></param>
		/// <param name="onVal">True for low-high, false for high-low</param>
		/// <param name="bounceTimeMs"></param>
		/// <param name="longPressWaitMs"></param>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		public DebouncedOnOffSwitch(Cpu.Pin thePin, Port.ResistorMode resistorMode, bool onVal, int bounceTimeMs, int longPressWaitMs)
		{
			Port = new InterruptPort(thePin, false, resistorMode, Microsoft.SPOT.Hardware.Port.InterruptMode.InterruptEdgeBoth);
			Port.OnInterrupt += DebounceSwitch_OnInterrupt;
			_onVal = onVal ? 1 : 0;
			var longPressWaitSpan = new TimeSpan(0, 0, 0, 0, longPressWaitMs);
			var stateMachine = new StateMachine(Port, _onVal, bounceTimeMs, longPressWaitSpan);
			stateMachine.ClickResult += stateMachine_ClickResult;
			#region Commented code
			//			// Create a thread to handle bounces
			//			var debounceThread = new Thread(() =>
			//			{
			//				while (true)
			//				{
			//					// Wait on semaphore to start debouncing
			//					_debounceSempahore.WaitOne();
			//					//_debounceSempahore.Reset();	// Set semaphore to blocked
			//#if DebugViaPrint
			//					Debug.Print("Thread. State: " + DebounceStateDefs[(int)DebounceState] + "(" + DebounceState + ")");
			//#endif

			//					//var elapsedTime = (DateTime.Now - _lastEventTime).Milliseconds;
			//					//if (elapsedTime < _bounceTimeMs)
			//					//{
			//					//	Thread.Sleep(_bounceTimeMs - elapsedTime);
			//					//	continue;
			//					//}

			//					Thread.Sleep(_bounceTimeMs);

			//					// Set for appropriate wait state
			//					switch (DebounceState)
			//					{
			//						case DebounceStates.WaitPress:
			//							break;

			//						case DebounceStates.DebouncePress:
			//#if DebugViaLcd
			//							ShowStateOnLcd();
			//#endif
			//							DebounceState = DebounceStates.WaitRelease;
			//#if DebugViaPrint
			//							Debug.Print("Thread. Change to WaitRelease");
			//#endif
			//							break;

			//						case DebounceStates.WaitRelease:
			//							break;

			//						case DebounceStates.DebounceRelease:
			//							DebounceState = DebounceStates.WaitPress;
			//#if DebugViaPrint
			//							Debug.Print("Thread. Change to WaitPress");
			//#endif
			//#if DebugViaLcd
			//							ShowStateOnLcd();
			//#endif
			//							break;
			//						default:
			//							throw new ArgumentOutOfRangeException();
			//					}
			//				}
			//			});
			//			debounceThread.Start();
			#endregion
		}

		private void stateMachine_ClickResult(DateTime clickTime, bool shortPress)
		{
			if (shortPress && OnShortPress != null)
			{
				OnShortPress(0, 0, clickTime);
				return;
			}
			if (OnLongPress != null)
			{
				OnLongPress(0, 0, clickTime);
			}
		}

		/// <summary>
		/// State machine for debouncing
		/// </summary>
		private class StateMachine
		{
			public StateMachine(InterruptPort port, int onVal, int bounceTimeMs, TimeSpan longPressWaitSpan)
			{
#if DebugViaPrint
				Debug.Print("Constructor. _bounceTimeMs: " + bounceTimeMs + ", longPressWaitSpan: " + longPressWaitSpan);
#endif
				TheStateMachine = this;
				_debounceTimer = new SimpleOneshotTimer(DebounceTimer_Tick, null, bounceTimeMs);
				_longPressWaitSpan = longPressWaitSpan;
				_port = port;
				_onVal = onVal;
			}
			public static StateMachine TheStateMachine;
			private readonly SimpleOneshotTimer _debounceTimer;
			private readonly TimeSpan _longPressWaitSpan;
			private readonly InterruptPort _port;
			private readonly int _onVal;

			private enum DebounceStates
			{
				/// <summary>Waiting for switch press</summary>
				WaitPress,

				/// <summary>Debouncing the press</summary>
				DebouncePress,

				/// <summary>Waiting for switch release</summary>
				WaitRelease,

				/// <summary>Debouncing the release</summary>
				DebounceRelease
			}

			private readonly string[] _debounceStateDefs = { "WaitPress", "DebouncePress", "WaitRelease", "DebounceRelease" };
#if DebugViaPrint
			private string DebounceStateDef { get { return _debounceStateDefs[(int)_debounceState] + "(" + _debounceState + ")"; } }
#endif

			public delegate void ClickResultDelegate(DateTime clickTime, bool shortPress);
			public event ClickResultDelegate ClickResult;

			private DebounceStates _debounceState = DebounceStates.WaitPress;

			/// <summary>
			/// Switch event has occurred
			/// </summary>
			/// <param name="pressed">true iff switch is pressed (on)</param>
			public void SwitchEvent(bool pressed)
			{
#if DebugViaPrint
				//Debug.Print("Switch. _debounceState: " + DebounceStateDef + ", pressed: " + pressed);
#endif
				switch (_debounceState)
				{
					case DebounceStates.WaitPress:
						if (!pressed) { return; }
						_debounceState = DebounceStates.DebouncePress;
						_debounceTimer.Start();
#if DebugViaPrint
						Debug.Print("Switch. Switch to " + DebounceStateDef);
#endif
						break;
					case DebounceStates.DebouncePress:
						break;
					case DebounceStates.WaitRelease:
						if (pressed) { return; }
						var now = DateTime.Now;
						var pressDuration = now - _pressedTime;
						var shortPress = pressDuration < _longPressWaitSpan;
#if DebugViaPrint
						Debug.Print("Switch. now: " + now + "." + now.Millisecond + ", _pressedTime: " + _pressedTime + "." + _pressedTime.Millisecond
							+ ", \npressDuration: " + pressDuration + ", shortPress: " + shortPress);
#endif
						if (ClickResult != null)
						{
							ClickResult(now, shortPress);
						}
						_debounceState = DebounceStates.DebounceRelease;
						_debounceTimer.Start();
#if DebugViaPrint
						Debug.Print("Switch. Switch to " + DebounceStateDef);
#endif
						break;
					case DebounceStates.DebounceRelease:
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			private DateTime _pressedTime;

			/// <summary>
			/// Debounce timer
			/// </summary>
			/// <param name="obj"></param>
			private void DebounceTimer_Tick(object obj)
			{
#if DebugViaPrint
				//Debug.Print("Timer. _debounceState: " + DebounceStateDef);
#endif
				switch (_debounceState)
				{
					case DebounceStates.WaitPress:
						// Ignore
						break;

					case DebounceStates.DebouncePress:
						var portValue = _port.Read() ? 1 : 0;
						if (portValue != _onVal)
						{
							_debounceState = DebounceStates.WaitPress;
#if DebugViaPrint
							Debug.Print("Timer. Brief click. Switch to " + DebounceStateDef);
#endif
							break;
						}
						_debounceState = DebounceStates.WaitRelease;
						_pressedTime = DateTime.Now;
#if DebugViaPrint
						Debug.Print("Timer. Switch to " + DebounceStateDef);
#endif
						break;

					case DebounceStates.WaitRelease:
						// Ignore
						break;

					case DebounceStates.DebounceRelease:
						_debounceState = DebounceStates.WaitPress;
#if DebugViaPrint
						Debug.Print("Timer. Switch to " + DebounceStateDef);
#endif
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		/// <summary>
		/// Handle an interrupt
		/// Atomically invoke the state machine. Ignore switch events until state machine returns.
		/// </summary>
		/// <param name="port"></param>
		/// <param name="state"></param>
		/// <param name="interruptTime"></param>
		private void DebounceSwitch_OnInterrupt(uint port, uint state, DateTime interruptTime)
		{
			// Atomically check if not busy (0). If so, set to busy (1)
			var orig = Interlocked.CompareExchange(ref _busy, 1, 0);
			// If marked as busy to start with, do nothing
			if (orig == 1)
			{
				return;
			}
#if DebugViaPrint
			//Debug.Print("Interrupt. state: " + state);
#endif
			// Call the state machine with the pressed/not pressed state
			var pressed = state == _onVal;
			StateMachine.TheStateMachine.SwitchEvent(pressed);
			// Set to not busy
			_busy = 0;
		}
		private int _busy;

		//private StateMachine.DebounceStates _debounceState = StateMachine.DebounceStates.WaitPress;
		//private readonly string[] _debounceStateDefs = { "WaitPress", "DebouncePress", "WaitRelease", "DebounceRelease" };


		//		private void DebounceSwitch_OnInterrupt(uint port, uint state, DateTime interruptTime)
		//		{
		//#if DebugViaPrint
		//			Debug.Print("Switch Interrupt. State: " + DebounceStateDefs[(int)DebounceState] + "(" + DebounceState + ")");
		//#endif
		//			_lastEventTime = interruptTime;
		//			switch (DebounceState)
		//			{
		//				case DebounceStates.WaitPress:
		//					if (state != _onVal)
		//					{
		//						return;
		//					}
		//					_pressOnTime = interruptTime;

		//					DebounceState = DebounceStates.DebouncePress;
		//#if DebugViaLcd
		//					ShowStateOnLcd();
		//#endif
		//					_debounceSempahore.Set();
		//					break;

		//				case DebounceStates.DebouncePress:
		//					break;

		//				case DebounceStates.WaitRelease:
		//					if (state == _onVal)
		//					{
		//						return;
		//					}
		//					DebounceState = DebounceStates.DebounceRelease;
		//#if DebugViaLcd
		//					ShowStateOnLcd();
		//#endif
		//					_debounceSempahore.Set();
		//					// Execute the callback
		//					PressedSpan = interruptTime - _pressOnTime;
		//#if DebugViaPrint
		//					Debug.Print("\nWaitRelease. \n\tinterruptTime: " + interruptTime + ", \n\t_pressOnTime: " + _pressOnTime + ", \n\tPressedSpan: " + PressedSpan);
		//					Debug.Print("\t\t_longPressWaitSpan: " + _longPressWaitSpan + ", comparison " + (PressedSpan < _longPressWaitSpan));
		//#endif
		//					//_pressOnTime = DateTime.MinValue;
		//					if (PressedSpan < _longPressWaitSpan && OnShortPress != null)
		//					{
		//						OnShortPress(port, state, interruptTime);
		//					}
		//					else if (PressedSpan >= _longPressWaitSpan && OnLongPress != null)
		//					{
		//						OnLongPress(port, state, interruptTime);
		//					}
		//					break;

		//				case DebounceStates.DebounceRelease:
		//					break;

		//				default:
		//					throw new ArgumentOutOfRangeException();
		//			}
		//		}
	}
}
