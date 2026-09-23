using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace RichHudFramework.Internal
{
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public sealed class ExceptionHandler : MySessionComponentBase
	{
		public static string ModName { get; set; }

		public static int RecoveryLimit { get; set; }

		public static int RecoveryAttempts { get; private set; }

		public static bool PromptForReload { get; set; }

		public static bool Reloading { get; private set; }

		public static bool Unloading { get; private set; }

		public static bool IsClient { get; private set; }

		public static bool IsServer { get; private set; }

		public static bool IsDedicated { get; private set; }

		public static bool ClientsPaused { get; private set; }

		public static bool DebugLogging { get; set; }

		private static ExceptionHandler instance;
		private const long exceptionReportInterval = 100, exceptionLoopTime = 50;
		private const int exceptionLoopCount = 10;

		private int exceptionCount;
		private readonly List<ModBase> clients;
		private readonly List<string> exceptionMessages;
		private readonly StringBuilder debugNotifications;
		private readonly Stopwatch errorTimer;

		private Action lastMissionScreen;
		private IMyHudNotification debugNotification;


		public ExceptionHandler()
		{
			if (instance == null)
				instance = this;
			else
				throw new Exception("Only one instance of ExceptionHandler can exist at any given time.");

			ModName = DebugName;
			RecoveryLimit = 1;


			exceptionMessages = new List<string>();

			errorTimer = new Stopwatch();

			clients = new List<ModBase>();

			debugNotifications = new StringBuilder();
		}


		public override void LoadData()
		{
			IsDedicated = MyAPIGateway.Utilities.IsDedicated;
			IsServer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer || IsDedicated;
			IsClient = !IsDedicated;

			WriteToLogAndConsole($"Exception Handler Init. Dedicated: {IsDedicated}, IsServer: {IsServer}, IsClient: {IsClient}", true);
		}


		public static void RegisterClient(ModBase client)
		{
			if (!instance.clients.Contains(client))
			{
				instance.clients.Add(client);
				WriteToLog($"[{client.GetType().Name}] Session component registered.", true);
			}
		}


		public override void Draw()
		{
			if (errorTimer.ElapsedMilliseconds > exceptionReportInterval)
				HandleExceptions();

			if (lastMissionScreen != null && !MyAPIGateway.Gui.ChatEntryVisible)
			{
				lastMissionScreen();
				lastMissionScreen = null;
			}

			if (debugNotification != null)
			{
				debugNotification.Text = "";
				debugNotification.Hide();
			}

			if (debugNotifications.Length > 0)
			{
				if (debugNotification == null)
					debugNotification = MyAPIGateway.Utilities.CreateNotification("", 500, MyFontEnum.Red);

				debugNotification.Text = debugNotifications.ToString();
				debugNotification.Show();
				debugNotifications.Clear();
			}

			if (Reloading)
				FinishReload();
		}


		public static void Run(Action Action)
		{
			try
			{
				Action();
			}
			catch (Exception e)
			{
				if (instance != null)
					instance.ReportExceptionInternal(e);
				else
					WriteToLog("Mod encountered an unhandled exception.\n" + e.ToString() + '\n');
			}
		}


		public static TResult Run<TResult>(Func<TResult> Func)
		{

			TResult value = default(TResult);

			try
			{

				value = Func();
			}
			catch (Exception e)
			{
				if (instance != null)
					instance.ReportExceptionInternal(e);
				else
					WriteToLog("Mod encountered an unhandled exception.\n" + e.ToString() + '\n');
			}

			return value;
		}


		public static void ReportException(Exception e) =>
			instance.ReportExceptionInternal(e);


		private void ReportExceptionInternal(Exception e)
		{
			if (e == null)

				e = new Exception("Null exception reported.");

			lock (exceptionMessages)
			{
				string message = e.ToString();

				if (!exceptionMessages.Contains(message))
					exceptionMessages.Add(message);

				if (exceptionCount == 0)
					errorTimer.Restart();

				exceptionCount++;

				if (exceptionCount > exceptionLoopCount && errorTimer.ElapsedMilliseconds < exceptionLoopTime)
					PauseClients();
			}
		}


		private void HandleExceptions()
		{
			if (exceptionCount > 0)
			{

				string exceptionText = GetExceptionText();
				exceptionCount = 0;

				WriteToLog("Mod encountered an unhandled exception.\n" + exceptionText + '\n');
				exceptionMessages.Clear();

				if (!Unloading && !Reloading)
				{
					if (IsClient && PromptForReload)
					{
						if (RecoveryAttempts < RecoveryLimit)
						{
							PauseClients();
							ShowErrorPrompt(exceptionText, true);
						}
						else
						{
							UnloadClients();
							ShowErrorPrompt(exceptionText, false);
						}
					}
					else
					{
						if (RecoveryAttempts < RecoveryLimit)
							StartReload();
						else
							UnloadClients();
					}

					RecoveryAttempts++;
				}
			}
		}


		private string GetExceptionText()
		{

			StringBuilder errorMessage = new StringBuilder();

			if (exceptionCount > exceptionLoopCount && errorTimer.ElapsedMilliseconds < exceptionLoopTime)
				errorMessage.AppendLine($"[Exception Loop Detected] {exceptionCount} exceptions were reported within a span of {errorTimer.ElapsedMilliseconds}ms.");

			for (int n = 0; n < exceptionMessages.Count - 1; n++)
				errorMessage.AppendLine(exceptionMessages[n]);

			errorMessage.Append(exceptionMessages[exceptionMessages.Count - 1]);

			errorMessage.Replace("--->", "\n   --->");
			return errorMessage.ToString();
		}


		private void ShowErrorPrompt(string errorMessage, bool canReload)
		{
			if (canReload)
			{
				ShowMissionScreen
				(
					"Debug",
					$"{ModName} has encountered a problem and will need to reload. Press the X in the upper right hand corner " +
					"to cancel.\n\n" +
					"Error Details:\n" +
					errorMessage,
					AllowReload,
					"Reload"
				);
			}
			else
			{
				ShowMissionScreen
				(
					"Debug",
					$"{ModName} has encountered an error and was unable to recover.\n\n" +
					"Error Details:\n" +
					errorMessage,
					null,
					"Close"
				);

				SendChatMessage($"{ModName} has encountered an error and was unable to recover. See log for details.");
			}
		}


		private void AllowReload(ResultEnum response)
		{
			if (response == ResultEnum.OK)
				StartReload();
			else
				UnloadClients();
		}


		public static void ReloadClients() =>
			instance.StartReload();


		public static void ShowMissionScreen(string subHeading = null, string message = null, Action<ResultEnum> callback = null, string okButtonCaption = null)
		{
			Action messageAction = () => MyAPIGateway.Utilities.ShowMissionScreen(ModName, subHeading, null, message, callback, okButtonCaption);
			instance.lastMissionScreen = messageAction;
		}


		public static void ShowMessageScreen(string subHeading, string message) =>
			ShowMissionScreen(subHeading, message, null, "Close");


		public static void SendChatMessage(string message)
		{
			if (!IsDedicated)
			{
				try
				{
					MyAPIGateway.Utilities.ShowMessage(ModName, message);
				}
				catch { }
			}
		}


		public static void SendDebugNotification(string message)
		{
			if (!IsDedicated)
			{
				instance?.debugNotifications.AppendLine(message);
			}
		}


		public static void WriteToLog(string message, bool debugOnly = false)
		{
			if (!(debugOnly && !DebugLogging))
			{
				try
				{
					MyLog.Default.WriteLine($"[{ModName}] {message}");
				}
				catch { }
			}
		}


		public static void WriteToConsole(string message, bool debugOnly = false)
		{
			if (!(debugOnly && !DebugLogging))
			{
				try
				{
					MyLog.Default.WriteLineToConsole($"[{ModName}] {message}");
				}
				catch { }
			}
		}


		public static void WriteToLogAndConsole(string message, bool debugOnly = false)
		{
			if (!(debugOnly && !DebugLogging))
			{
				try
				{
					MyLog.Default.WriteLineAndConsole($"[{ModName}] {message}");
				}
				catch { }
			}
		}


		private void PauseClients()
		{
			for (int n = 0; n < clients.Count; n++)
				clients[n].CanUpdate = false;

			ClientsPaused = true;
		}


		private void UnpauseClients()
		{
			for (int n = 0; n < clients.Count; n++)
				clients[n].CanUpdate = true;

			ClientsPaused = false;
		}


		private void StartReload()
		{
			if (!Reloading)
			{
				WriteToLog("Stopping mod...");
				Reloading = true;

				CloseClients();
			}
		}


		private void FinishReload()
		{
			if (Reloading)
			{
				for (int n = 0; n < clients.Count; n++)
				{
					bool success = true;
					string typeName = clients[n].GetType().Name;

					Run(() =>
					{
						WriteToLog($"[{typeName}] Restarting session component...", true);
						clients[n].ManualStart();
						success = clients[n].Loaded;
					});

					if (success)
						WriteToLog($"[{typeName}] Session component started.", true);
					else
						WriteToLog($"[{typeName}] Failed to start session component.");
				}

				Reloading = false;
				ClientsPaused = false;
				WriteToLog("Mod reloaded.");
			}
		}


		private void UnloadClients()
		{
			if (!Unloading)
			{
				WriteToLog("Unloading mod...");
				Unloading = true;
				Reloading = false;

				CloseClients();
			}

			WriteToLog("Mod unloaded.");
		}


		private void CloseClients()
		{
			for (int n = 0; n < clients.Count; n++)
			{
				bool success = false;
				string typeName = clients[n].GetType().Name;
				WriteToLog($"[{typeName}] Stopping session component...", true);

				Run(() =>
				{
					if (clients[n].CanUpdate)
						clients[n].BeforeClose();

					success = true;
				});

				clients[n].CanUpdate = false;

				if (success)
					WriteToLog($"[{typeName}] Session component stopped.", true);
				else
					WriteToLog($"[{typeName}] Failed to stop session component.");
			}

			ClientsPaused = true;

			for (int n = 0; n < clients.Count; n++)
			{
				bool success = false;
				string typeName = clients[n].GetType().Name;
				WriteToLog($"[{typeName}] Closing session component...", true);

				Run(() =>
				{
					clients[n].Close();
					success = true;
				});

				if (success)
					WriteToLog($"[{typeName}] Session component closed.", true);
				else
					WriteToLog($"[{typeName}] Failed to close session component.");
			}
		}


		protected override void UnloadData()
		{
			UnloadClients();
			HandleExceptions();
			instance = null;

			WriteToLog("Exception Handler unloaded.", true);
		}
	}
}
