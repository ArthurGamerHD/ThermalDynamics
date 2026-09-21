using RichHudFramework.Internal;
using Sandbox.ModAPI;
using System;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using ClientData = VRage.MyTuple<string, System.Action<int, object>, System.Action, int>;
using ServerData = VRage.MyTuple<System.Action, System.Func<int, object>, int>;

namespace RichHudFramework.Client
{
	using ExtendedClientData = MyTuple<ClientData, Action<Action>, ApiMemberAccessor>;

	public sealed class RichHudClient : RichHudComponentBase
	{
/// <summary>Vector4I operation.</summary>
		internal static readonly Vector4I versionID = new Vector4I(1, 3, 0, 0); // Major, Minor, Rev, Hotfix
		internal const ClientSubtypes subtype = ClientSubtypes.Full;
		private const long modID = 1965654081, queueID = 1314086443;
		private const int vID = (int)APIVersionTable.Latest;

		public static bool Registered => Instance != null ? Instance.registered : false;

		private static RichHudClient Instance { get; set; }

		private readonly ExtendedClientData regMessage;
		private readonly Action InitAction, ResetAction;

		private bool regFail, registered, inQueue;
		private Func<int, object> GetApiDataFunc;
		private Action UnregisterAction;

/// <summary>RichHudClient operation.</summary>
		private RichHudClient(string modName, Action InitCallback, Action ResetCallback) : base(false, true)
		{
			InitAction = InitCallback;
			ResetAction = ResetCallback;

			ExceptionHandler.ModName = modName;

/// <summary>ClientData operation.</summary>
			var clientData = new ClientData(modName, MessageHandler, RemoteReset, vID);
/// <summary>ExtendedClientData operation.</summary>
			regMessage = new ExtendedClientData(clientData, ExceptionHandler.Run, GetOrSetMember);
		}

/// <summary>Init operation.</summary>
		public static void Init(string modName, Action InitCallback, Action ResetCallback)
		{
			if (Instance == null)
			{
/// <summary>RichHudClient operation.</summary>
				Instance = new RichHudClient(modName, InitCallback, ResetCallback);
				Instance.RequestRegistration();

				if (!Registered && !Instance.regFail)
				{
					Instance.EnterQueue();
				}
			}
		}

/// <summary>Reset operation.</summary>
		public static void Reset()
		{
			if (Registered)
				ExceptionHandler.ReloadClients();
		}

/// <summary>MessageHandler operation.</summary>
		private void MessageHandler(int typeValue, object message)
		{
			MsgTypes msgType = (MsgTypes)typeValue;

			if (!regFail)
			{
				if (!Registered)
				{
					if ((msgType == MsgTypes.RegistrationSuccessful) && message is ServerData)
					{
						var data = (ServerData)message;
						UnregisterAction = data.Item1;
						GetApiDataFunc = data.Item2;

						registered = true;

						ExceptionHandler.Run(InitAction);
						ExceptionHandler.WriteToLog($"[RHF] Successfully registered with Rich HUD Master.");
					}
/// <summary>if operation.</summary>
					else if (msgType == MsgTypes.RegistrationFailed)
					{
						if (message is string)
							ExceptionHandler.WriteToLog($"[RHF] Failed to register with Rich HUD Master. Message: {message as string}");
						else
							ExceptionHandler.WriteToLog($"[RHF] Failed to register with Rich HUD Master.");

						regFail = true;
					}
				}
			}
		}

/// <summary>Returns the orsetmember.</summary>
		private object GetOrSetMember(object data, int memberEnum)
		{
			switch ((ClientDataAccessors)memberEnum)
			{
				case ClientDataAccessors.GetVersionID:
					return versionID;
				case ClientDataAccessors.GetSubtype:
					return subtype;
				case ClientDataAccessors.ReportException:
					return new Action<Exception>(ExceptionHandler.ReportException);
				case ClientDataAccessors.GetIsPausedFunc:
					return new Func<bool>(() => ExceptionHandler.ClientsPaused);
			}

			return null;
		}

/// <summary>RequestRegistration operation.</summary>
		private void RequestRegistration() =>
			MyAPIUtilities.Static.SendModMessage(modID, regMessage);

/// <summary>EnterQueue operation.</summary>
		private void EnterQueue() =>
			MyAPIUtilities.Static.RegisterMessageHandler(queueID, QueueHandler);

/// <summary>ExitQueue operation.</summary>
		private void ExitQueue() =>
			MyAPIUtilities.Static.UnregisterMessageHandler(queueID, QueueHandler);

/// <summary>QueueHandler operation.</summary>
		private void QueueHandler(object message)
		{
			if (!(registered || regFail))
			{
				inQueue = true;
				RequestRegistration();
			}
		}

/// <summary>Update operation.</summary>
		public override void Update()
		{
			if (registered && inQueue)
			{
				ExitQueue();
				inQueue = false;
			}
		}

/// <summary>Close operation.</summary>
		public override void Close()
		{
			ExitQueue();
			Unregister();
			Instance = null;
		}

/// <summary>RemoteReset operation.</summary>
		private void RemoteReset()
		{
			ExceptionHandler.Run(() =>
			{
				if (registered)
				{
					ExceptionHandler.ReloadClients();
					ResetAction();
				}
			});
		}

/// <summary>Unregisters the API and cleans resources.</summary>
		private void Unregister()
		{
			if (registered)
			{
				registered = false;
				UnregisterAction();
			}
		}

		public abstract class ApiModule : RichHudComponentBase
		{
			protected readonly ApiModuleTypes componentType;

/// <summary>ApiModule operation.</summary>
			public ApiModule(ApiModuleTypes componentType, bool runOnServer, bool runOnClient) : base(runOnServer, runOnClient)
			{
				if (!Registered)
					throw new Exception("Types of ApiModule cannot be instantiated before RichHudClient is initialized.");

				this.componentType = componentType;
			}

/// <summary>Returns the apidata.</summary>
			protected object GetApiData()
			{
				return Instance?.GetApiDataFunc((int)componentType);
			}
		}
	}
}