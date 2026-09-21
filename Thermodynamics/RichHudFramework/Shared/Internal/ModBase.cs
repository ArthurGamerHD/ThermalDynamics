using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;

namespace RichHudFramework.Internal
{
	public abstract partial class ModBase : MySessionComponentBase
	{
		public bool RunOnServer { get; }

		public bool RunOnClient { get; }

		public new bool Loaded { get; private set; }

		public bool CanUpdate
		{
			get { return _canUpdate && ((RunOnClient && ExceptionHandler.IsClient) || (RunOnServer && ExceptionHandler.IsDedicated)); }
			set { _canUpdate = value; }
		}

		private readonly List<ModuleBase> modules;
		private bool _canUpdate, closing;

/// <summary>ModBase operation.</summary>
		protected ModBase(bool runOnServer, bool runOnClient)
		{
/// <summary>List operation.</summary>
			modules = new List<ModuleBase>();
			RunOnServer = runOnServer;
			RunOnClient = runOnClient;
		}

/// <summary>LoadData operation.</summary>
		public sealed override void LoadData()
		{
			if (!Loaded && !ExceptionHandler.Unloading && !closing)
			{
				CanUpdate = true;
				ExceptionHandler.RegisterClient(this);

				if (CanUpdate)
					AfterLoadData();
			}
		}

/// <summary>AfterLoadData operation.</summary>
		protected new virtual void AfterLoadData() { }

/// <summary>Init operation.</summary>
		public sealed override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			if (!Loaded && !ExceptionHandler.Unloading && !closing)
			{
				if (CanUpdate)
					AfterInit();

				Loaded = true;
			}
		}

/// <summary>AfterInit operation.</summary>
		protected virtual void AfterInit() { }

/// <summary>ManualStart operation.</summary>
		public void ManualStart()
		{
			if (!Loaded && !ExceptionHandler.Unloading && !closing)
			{
				LoadData();
				Init(null);
			}
		}

/// <summary>Draw operation.</summary>
		public override void Draw()
		{
			if (Loaded && CanUpdate)
			{
				ExceptionHandler.Run(() =>
				{
					for (int n = 0; n < modules.Count; n++)
					{
						bool updateClient = modules[n].runOnClient && ExceptionHandler.IsClient,
							updateServer = modules[n].runOnServer && ExceptionHandler.IsDedicated;

						if (updateClient || updateServer)
							modules[n].Draw();
					}
				});
			}
		}

/// <summary>HandleInput operation.</summary>
		public override void HandleInput()
		{
			if (Loaded && CanUpdate)
			{
				ExceptionHandler.Run(() =>
				{
					for (int n = 0; n < modules.Count; n++)
					{
						bool updateClient = modules[n].runOnClient && ExceptionHandler.IsClient,
							updateServer = modules[n].runOnServer && ExceptionHandler.IsDedicated;

						if (updateClient || updateServer)
							modules[n].HandleInput();
					}
				});
			}
		}

/// <summary>UpdateBeforeSimulation operation.</summary>
		public sealed override void UpdateBeforeSimulation() =>
			BeforeUpdate();

/// <summary>Simulate operation.</summary>
		public sealed override void Simulate() =>
			BeforeUpdate();

/// <summary>UpdateAfterSimulation operation.</summary>
		public sealed override void UpdateAfterSimulation() =>
			BeforeUpdate();

/// <summary>BeforeUpdate operation.</summary>
		protected virtual void BeforeUpdate()
		{
			if (Loaded && CanUpdate)
			{
				ExceptionHandler.Run(() =>
				{
					for (int n = 0; n < modules.Count; n++)
					{
						bool updateClient = modules[n].runOnClient && ExceptionHandler.IsClient,
							updateServer = modules[n].runOnServer && ExceptionHandler.IsDedicated;

						if (updateClient || updateServer)
							modules[n].Update();
					}

					Update();
				});
			}
		}

/// <summary>Update operation.</summary>
		protected virtual void Update() { }

/// <summary>BeforeClose operation.</summary>
		public virtual void BeforeClose() { }

/// <summary>Close operation.</summary>
		public virtual void Close()
		{
			if (!closing)
			{
				Loaded = false;
				CanUpdate = false;
				closing = true;

				CloseModules();
				modules.Clear();

				closing = false;
			}
		}

/// <summary>CloseModules operation.</summary>
		private void CloseModules()
		{
/// <summary>Returns the type.</summary>
			string typeName = GetType().Name;

			for (int n = modules.Count - 1; n >= 0; n--)
			{
				var module = modules[n];
				bool success = false;

				ExceptionHandler.Run(() =>
				{
					ExceptionHandler.WriteToLog($"[{typeName}] Closing {module.GetType().Name} module...", true);
					module.Close();
					success = true;
				});

				if (success)
					ExceptionHandler.WriteToLog($"[{typeName}] Closed {module.GetType().Name} module.", true);
				else
					ExceptionHandler.WriteToLog($"[{typeName}] Failed to close {module.GetType().Name} module.");

				module.UnregisterComponent(n);
			}
		}

/// <summary>UnloadData operation.</summary>
		protected override void UnloadData()
		{ }

		public abstract class ModuleBase
		{
			protected ModBase Parent { get; private set; }

			public readonly bool runOnServer, runOnClient;

/// <summary>ModuleBase operation.</summary>
			protected ModuleBase(bool runOnServer, bool runOnClient, ModBase parent)
			{
				this.runOnServer = runOnServer;
				this.runOnClient = runOnClient;

				RegisterComponent(parent);
			}

/// <summary>Registers the API and message handler.</summary>
			public void RegisterComponent(ModBase parent)
			{
				if (Parent == null)
				{
					parent.modules.Add(this);

					Parent = parent;
					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Registered {GetType().Name} module.", true);
				}
			}

/// <summary>Unregisters the API and cleans resources.</summary>
			public void UnregisterComponent()
			{
				if (Parent != null)
				{
					Parent.modules.Remove(this);

					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Unregistered {GetType().Name} module.", true);
					Parent = null;
				}
			}

/// <summary>Unregisters the API and cleans resources.</summary>
			public void UnregisterComponent(int index)
			{
				if (Parent != null && index < Parent.modules.Count && Parent.modules[index] == this)
				{
					Parent.modules.RemoveAt(index);

					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Unregistered {GetType().Name} module.", true);
					Parent = null;
				}
			}

/// <summary>Draw operation.</summary>
			public virtual void Draw() { }

/// <summary>HandleInput operation.</summary>
			public virtual void HandleInput() { }

/// <summary>Update operation.</summary>
			public virtual void Update() { }

/// <summary>Close operation.</summary>
			public virtual void Close() { }
		}

		public abstract class ParallelModuleBase : ModuleBase
		{
			private readonly TaskPool taskPool;

/// <summary>ParallelModuleBase operation.</summary>
			protected ParallelModuleBase(bool runOnServer, bool runOnClient, ModBase parent) : base(runOnServer, runOnClient, parent)
			{
/// <summary>TaskPool operation.</summary>
				taskPool = new TaskPool(ErrorCallback);
			}

/// <summary>ErrorCallback operation.</summary>
			protected virtual void ErrorCallback(List<KnownException> knownExceptions, AggregateException aggregate)
			{
				if (knownExceptions.Count > 0)
					ExceptionHandler.ReportException(new AggregateException(knownExceptions));

				if (aggregate != null)
					ExceptionHandler.ReportException(aggregate);
			}

/// <summary>EnqueueTask operation.</summary>
			protected void EnqueueTask(Action action) =>
				taskPool.EnqueueTask(action);

/// <summary>EnqueueAction operation.</summary>
			protected void EnqueueAction(Action action) =>
				taskPool.EnqueueAction(action);
		}
	}
}
