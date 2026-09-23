using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Utils;
using VRageMath;

namespace SENetworkAPI
{

	public enum NetworkTypes { Dedicated, Server, Client }





	public abstract class NetworkAPI
	{





		public const string Version = "2.0.0";


		public static NetworkAPI Instance = null;

		public static bool IsInitialized => Instance != null;

		public static bool LogNetworkTraffic = false;






		public static int CompressionThreshold = 1024;






		public static bool UseCompactBatches = false;


		public static int CompactBatchThreshold = 256;





		public const int UnreliableMessageLimit = 1024;


		internal static bool ResolveReliability(byte[] packet, bool isReliable)
		{
			return isReliable || packet.Length > UnreliableMessageLimit;
		}


		internal static void Compress(Command cmd)
		{
			if (cmd.BatchFormat != 0) return;
			if (UseCompactBatches && cmd.IsProperty && cmd.Properties != null && cmd.Property == null)
			{
				CompactBatch.TryEncode(cmd);
				return;
			}


			if (!cmd.IsCompressed && cmd.Property != null && cmd.Property.Data != null &&
				cmd.Property.Data.Length > CompressionThreshold)
			{
				byte[] encoded = MyAPIGateway.Utilities.SerializeToBinary(cmd.Property);
				byte[] packed = MyCompression.Compress(encoded);
				if (packed.Length + 2 < encoded.Length)
				{
					cmd.Data = packed;
					cmd.Property = null;
					cmd.IsCompressed = true;
				}
				return;
			}

			if (cmd.IsCompressed || cmd.Data == null || cmd.Data.Length <= CompressionThreshold)
			{
				return;
			}

			byte[] compressed = MyCompression.Compress(cmd.Data);

			if (compressed.Length >= cmd.Data.Length)
			{
				return;
			}

			cmd.Data = compressed;
			cmd.IsCompressed = true;
		}





		public event Action<ulong, string, byte[], DateTime> OnCommandRecived;


		public readonly ushort ComId;

		public readonly string Keyword;

		public readonly string ModName;

		internal bool UsingTextCommands => Keyword != null;







		public NetworkTypes NetworkType
		{
			get
			{
				if (this is Client)
				{
					return NetworkTypes.Client;
				}

				return MyAPIGateway.Utilities.IsDedicated ? NetworkTypes.Dedicated : NetworkTypes.Server;
			}
		}

		internal Dictionary<string, Action<ulong, string, byte[], DateTime>> NetworkCommands = new Dictionary<string, Action<ulong, string, byte[], DateTime>>(StringComparer.OrdinalIgnoreCase);
		internal Dictionary<string, Action<string>> ChatCommands = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase);






		public NetworkAPI(ushort comId, string modName, string keyword = null)
		{
			ComId = comId;
			ModName = (modName == null) ? string.Empty : modName;
			Keyword = (keyword != null) ? keyword.ToLowerInvariant() : null;

			if (UsingTextCommands)
			{
				MyAPIGateway.Utilities.MessageEntered -= HandleChatInput;
				MyAPIGateway.Utilities.MessageEntered += HandleChatInput;
			}

			MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ComId, HandleIncomingPacket);
			MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ComId, HandleIncomingPacket);

			MyLog.Default.Info($"[NetworkAPI] Initialized. Version: {Version} Type: {GetType().Name} ComId: {ComId} Name: {ModName} Keyword: {Keyword}");
		}


		private void HandleChatInput(string messageText, ref bool sendToOthers)
		{
			if (!StartsWithKeyword(messageText))
				return;

			sendToOthers = false;


			string command = SecondToken(messageText);

			Action<string> callback;
			if (command == null)
			{
				if (ChatCommands.TryGetValue(string.Empty, out callback))
				{
					Invoke(callback, string.Empty, string.Empty);
					return;
				}
			}
			else if (ChatCommands.TryGetValue(command, out callback))
			{
				Invoke(callback, command, messageText.Substring(Keyword.Length + 1 + command.Length).Trim(' '));
				return;
			}

			if (!MyAPIGateway.Utilities.IsDedicated)
			{
				MyAPIGateway.Utilities.ShowMessage(ModName, "Command not recognized.");
			}
		}


		private bool StartsWithKeyword(string messageText)
		{
			if (messageText == null || Keyword == null)
				return false;

			int length = Keyword.Length;

			if (messageText.Length < length)
				return false;

			if (messageText.Length > length && messageText[length] != ' ')
				return false;

			return string.Compare(messageText, 0, Keyword, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
		}


		private string SecondToken(string messageText)
		{
			int start = Keyword.Length + 1;

			if (start > messageText.Length)
				return null;

			int end = messageText.IndexOf(' ', start);

			return (end < 0) ? messageText.Substring(start) : messageText.Substring(start, end - start);
		}


		private void Invoke(Action<string> callback, string command, string arguments)
		{
			if (callback == null)
			{
				return;
			}

			try
			{
				callback(arguments);
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[NetworkAPI] Chat command '{Keyword} {command}' threw:\n{e}");
			}
		}


		private void HandleIncomingPacket(ushort channelId, byte[] payload, ulong senderId, bool fromServer)
		{

			if (channelId != ComId || (!MyAPIGateway.Multiplayer.IsServer && !fromServer))
			{
				return;
			}

			try
			{
				Command cmd = MyAPIGateway.Utilities.SerializeFromBinary<Command>(payload);

				if (cmd == null)
				{
					if (LogNetworkTraffic)
					{
						MyLog.Default.Info($"[NetworkAPI] Ignored an empty packet on ComId {ComId}");
					}

					return;
				}



				if (MyAPIGateway.Multiplayer.IsServer)
				{
					cmd.SteamId = senderId;
				}

				if (LogNetworkTraffic)
				{
					MyLog.Default.Info($"[NetworkAPI] ----- TRANSMISSION RECIEVED -----");
					MyLog.Default.Info($"[NetworkAPI] Type: {((cmd.IsProperty) ? "Property" : $"Command ID: {cmd.CommandString}")}, {(cmd.IsCompressed ? "Compressed, " : "")}From: {cmd.SteamId} ");
				}

				if (cmd.IsCompressed)
				{
					cmd.Data = MyCompression.Decompress(cmd.Data);
					cmd.IsCompressed = false;
				}

				if (cmd.BatchFormat != 0)
				{
					if (cmd.BatchFormat != 1 || !cmd.IsProperty || cmd.Property != null || cmd.Properties != null)
						throw new InvalidOperationException("Unsupported or ambiguous compact batch format.");
					cmd.Properties = CompactBatch.Decode(cmd.Data);
				}

				if (cmd.IsProperty)
				{
					if (cmd.Property != null)
					{
						NetSync.RouteMessage(cmd.Property, cmd.SteamId, cmd.Timestamp);
					}
					else if (cmd.Properties != null)
					{
						for (int i = 0; i < cmd.Properties.Count; i++)
						{
							NetSync.RouteMessage(cmd.Properties[i], cmd.SteamId, cmd.Timestamp);
						}
					}
					else
					{
						NetSync.RouteMessage(MyAPIGateway.Utilities.SerializeFromBinary<SyncData>(cmd.Data), cmd.SteamId, cmd.Timestamp);
					}
				}
				else
				{
					if (!string.IsNullOrWhiteSpace(cmd.Message))
					{
						if (!MyAPIGateway.Utilities.IsDedicated)
						{
							if (MyAPIGateway.Session != null)
							{
								MyAPIGateway.Utilities.ShowMessage(ModName, cmd.Message);
							}
						}

						if (MyAPIGateway.Multiplayer.IsServer)
						{
							SendCommand(null, cmd.Message);
						}
					}

					if (cmd.CommandString != null)
					{

						DateTime sent = ToDateTime(cmd.Timestamp);

						Invoke(OnCommandRecived, cmd, sent, "OnCommandRecived");

						int space = cmd.CommandString.IndexOf(' ');
						string command = (space < 0) ? cmd.CommandString : cmd.CommandString.Substring(0, space);

						Action<ulong, string, byte[], DateTime> callback;
						if (NetworkCommands.TryGetValue(command, out callback))
						{
							Invoke(callback, cmd, sent, command);
						}
					}
				}

				if (LogNetworkTraffic)
				{
					MyLog.Default.Info($"[NetworkAPI] ----- END -----");
				}

			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[NetworkAPI] Failure in message processing:\n{e.ToString()}");
			}
		}


		private static DateTime ToDateTime(long timestamp)
		{
			if (timestamp < 0)
			{
				return DateTime.MinValue;
			}

			if (timestamp > DateTime.MaxValue.Ticks)
			{
				return DateTime.MaxValue;
			}

			return new DateTime(timestamp);
		}


		private void Invoke(Action<ulong, string, byte[], DateTime> callback, Command cmd, DateTime sent, string label)
		{
			if (callback == null)
			{
				return;
			}

			try
			{
				callback(cmd.SteamId, cmd.CommandString, cmd.Data, sent);
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[NetworkAPI] Network command '{label}' threw:\n{e}");
			}
		}









		public void RegisterNetworkCommand(string command, Action<ulong, string, byte[], DateTime> callback)
		{
			if (command == null)
			{
				throw new Exception($"[NetworkAPI] Cannot register a command using null. null is reserved for chat messages.");
			}

			if (NetworkCommands.ContainsKey(command))
			{
				throw new Exception($"[NetworkAPI] Failed to add the network command callback '{command}'. A command with the same name was already added.");
			}

			NetworkCommands.Add(command, callback);
		}



		public void UnregisterNetworkCommand(string command)
		{
			if (command != null)
			{
				NetworkCommands.Remove(command);
			}
		}










		public void RegisterChatCommand(string command, Action<string> callback)
		{
			if (command == null)
			{
				command = string.Empty;
			}

			if (ChatCommands.ContainsKey(command))
			{
				throw new Exception($"[NetworkAPI] Failed to add the network command callback '{command}'. A command with the same name was already added.");
			}

			ChatCommands.Add(command, callback);
		}



		public void UnregisterChatCommand(string command)
		{
			ChatCommands.Remove(command ?? string.Empty);
		}












		public abstract void SendCommand(string commandString, string message = null, byte[] data = null, DateTime? sent = null, ulong steamId = ulong.MinValue, bool isReliable = true);














		public abstract void SendCommand(string commandString, Vector3D point, double radius = 0, string message = null, byte[] data = null, DateTime? sent = null, ulong steamId = ulong.MinValue, bool isReliable = true);


		internal abstract void SendCommand(Command cmd, ulong steamId = ulong.MinValue, bool isReliable = true);


		internal abstract void SendCommand(Command cmd, Vector3D point, double range = 0, ulong steamId = ulong.MinValue, bool isReliable = true);



		public abstract void Say(string message);


		[ObsoleteAttribute("Manual Close() is unnecessary when SessionTools is included; it automatically cleans up the API when the world unloads.", false)]
		public void Close()
		{
			UnregisterHandlers();
		}


		internal void UnregisterHandlers()
		{
			MyLog.Default.Info($"[NetworkAPI] Unregistering communication stream: {ComId}");
			if (UsingTextCommands)
			{
				MyAPIGateway.Utilities.MessageEntered -= HandleChatInput;
			}

			MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ComId, HandleIncomingPacket);

		}


		[ObsoleteAttribute("Manual Dispose() is unnecessary when SessionTools is included; it automatically cleans up the API when the world unloads.", false)]
		public static void Dispose()
		{
			Shutdown();
		}


		internal static void Shutdown()
		{
			if (IsInitialized)
			{
				Instance.UnregisterHandlers();
			}

			Instance = null;

			NetSync.ClearRegistries();
		}










		public static void Init(ushort comId, string modName, string keyword = null)
		{
			if (IsInitialized)
				return;

			if (!MyAPIGateway.Multiplayer.IsServer)
			{

				Instance = new Client(comId, modName, keyword);
			}
			else
			{

				Instance = new Server(comId, modName, keyword);
			}
		}



		public static float GetDeltaMilliseconds(long timestamp)
		{
			return (DateTime.UtcNow.Ticks - timestamp) / TimeSpan.TicksPerMillisecond;
		}



		public static int GetDeltaFrames(long timestamp)
		{
			return (int)Math.Ceiling(GetDeltaMilliseconds(timestamp) / MillisecondsPerFrame);
		}

		private const double MillisecondsPerFrame = 1000d / 60d;
	}
}
