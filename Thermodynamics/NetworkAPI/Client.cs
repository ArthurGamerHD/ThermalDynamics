using Sandbox.ModAPI;
using System;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SENetworkAPI
{

	public class Client : NetworkAPI
	{





		public Client(ushort comId, string modName, string keyword = null) : base(comId, modName, keyword)
		{
		}












		public override void SendCommand(string commandString, string message = null, byte[] data = null, DateTime? sent = null, ulong steamId = ulong.MinValue, bool isReliable = true)
		{
			IMyPlayer player = MyAPIGateway.Session?.Player;

			if (player != null)
			{
				ulong steamUserId = player.SteamUserId;
				SendCommand(new Command() { CommandString = commandString, Message = message, Data = data, Timestamp = (sent == null) ? DateTime.UtcNow.Ticks : sent.Value.Ticks, SteamId = steamUserId }, steamUserId, isReliable);
			}
			else
			{
				MyLog.Default.Warning($"[NetworkAPI] ComID: {ComId} | Failed to send command. Session does not exist.");
			}
		}


		internal override void SendCommand(Command cmd, ulong steamId = ulong.MinValue, bool isReliable = true)
		{
			Compress(cmd);

			if (cmd.Timestamp == 0)
			{
				cmd.Timestamp = DateTime.UtcNow.Ticks;
			}

			byte[] packet = MyAPIGateway.Utilities.SerializeToBinary(cmd);


			isReliable = ResolveReliability(packet, isReliable);

			if (LogNetworkTraffic)
			{
				MyLog.Default.Info($"[NetworkAPI] TRANSMITTING Bytes: {packet.Length}  Command: {cmd.CommandString}  User: {steamId}");
			}

			MyAPIGateway.Multiplayer.SendMessageToServer(ComId, packet, isReliable);
		}











		public override void SendCommand(string commandString, Vector3D point, double radius = 0, string message = null, byte[] data = null, DateTime? sent = null, ulong steamId = 0, bool isReliable = true)
		{
			SendCommand(commandString, message, data, sent, steamId, isReliable);
		}


		internal override void SendCommand(Command cmd, Vector3D point, double radius = 0, ulong steamId = 0, bool isReliable = true)
		{
			SendCommand(cmd, steamId, isReliable);
		}







		public override void Say(string message)
		{
			SendCommand(null, message);
		}
	}
}
