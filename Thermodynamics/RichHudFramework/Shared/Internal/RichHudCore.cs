using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace RichHudFramework.Internal
{
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public sealed class RichHudCore : ModBase
	{
		public static RichHudCore Instance { get; private set; }

		public static event MessageEnteredDel LateMessageEntered;

		public static event MessageEnteredSenderDel MessageEnteredSender;

		private bool isMsgHandlerRegistered;

/// <summary>RichHudCore operation.</summary>
		public RichHudCore() : base(false, true)
		{
			if (Instance == null)
				Instance = this;
			else
				throw new Exception("Only one instance of RichHudCore can exist at any given time.");

			isMsgHandlerRegistered = false;
		}

/// <summary>MessageHandler operation.</summary>
		private void MessageHandler(ulong sender, string message, ref bool sendToOthers)
		{
			LateMessageEntered?.Invoke(message, ref sendToOthers);
			MessageEnteredSender?.Invoke(sender, message, ref sendToOthers);
		}

/// <summary>Draw operation.</summary>
		public override void Draw()
		{
			BeforeUpdate();
			base.Draw();

			if (!isMsgHandlerRegistered)
			{
				MyAPIGateway.Utilities.MessageEnteredSender += MessageHandler;
				isMsgHandlerRegistered = true;
			}
		}

/// <summary>Close operation.</summary>
		public override void Close()
		{
			base.Close();

			if (ExceptionHandler.Unloading)
			{
				MyAPIGateway.Utilities.MessageEnteredSender -= MessageHandler;
				Instance = null;
			}
		}

/// <summary>UnloadData operation.</summary>
		protected override void UnloadData()
		{
			LateMessageEntered = null;
			MessageEnteredSender = null;
		}
	}

	public abstract class RichHudComponentBase : ModBase.ModuleBase
	{
/// <summary>RichHudComponentBase operation.</summary>
		public RichHudComponentBase(bool runOnServer, bool runOnClient) : base(runOnServer, runOnClient, RichHudCore.Instance)
		{ }
	}

	public abstract class RichHudParallelComponentBase : ModBase.ParallelModuleBase
	{
/// <summary>RichHudParallelComponentBase operation.</summary>
		public RichHudParallelComponentBase(bool runOnServer, bool runOnClient) : base(runOnServer, runOnClient, RichHudCore.Instance)
		{ }
	}
}