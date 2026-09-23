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


		protected ModBase(bool runOnServer, bool runOnClient)
		{

			modules = new List<ModuleBase>();
			RunOnServer = runOnServer;
			RunOnClient = runOnClient;
		}


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


		protected new virtual void AfterLoadData() { }


		public sealed override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			if (!Loaded && !ExceptionHandler.Unloading && !closing)
			{
				if (CanUpdate)
					AfterInit();

				Loaded = true;
			}
		}


		protected virtual void AfterInit() { }


		public void ManualStart()
		{
			if (!Loaded && !ExceptionHandler.Unloading && !closing)
			{
				LoadData();
				Init(null);
			}
		}


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


		public sealed override void UpdateBeforeSimulation() =>
			BeforeUpdate();


		public sealed override void Simulate() =>
			BeforeUpdate();


		public sealed override void UpdateAfterSimulation() =>
			BeforeUpdate();


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


		protected virtual void Update() { }


		public virtual void BeforeClose() { }


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


		private void CloseModules()
		{

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


		protected override void UnloadData()
		{ }

		public abstract class ModuleBase
		{
			protected ModBase Parent { get; private set; }

			public readonly bool runOnServer, runOnClient;


			protected ModuleBase(bool runOnServer, bool runOnClient, ModBase parent)
			{
				this.runOnServer = runOnServer;
				this.runOnClient = runOnClient;

				RegisterComponent(parent);
			}


			public void RegisterComponent(ModBase parent)
			{
				if (Parent == null)
				{
					parent.modules.Add(this);

					Parent = parent;
					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Registered {GetType().Name} module.", true);
				}
			}


			public void UnregisterComponent()
			{
				if (Parent != null)
				{
					Parent.modules.Remove(this);

					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Unregistered {GetType().Name} module.", true);
					Parent = null;
				}
			}


			public void UnregisterComponent(int index)
			{
				if (Parent != null && index < Parent.modules.Count && Parent.modules[index] == this)
				{
					Parent.modules.RemoveAt(index);

					ExceptionHandler.WriteToLog($"[{Parent.GetType().Name}] Unregistered {GetType().Name} module.", true);
					Parent = null;
				}
			}


			public virtual void Draw() { }


			public virtual void HandleInput() { }


			public virtual void Update() { }


			public virtual void Close() { }
		}

		public abstract class ParallelModuleBase : ModuleBase
		{
			private readonly TaskPool taskPool;


			protected ParallelModuleBase(bool runOnServer, bool runOnClient, ModBase parent) : base(runOnServer, runOnClient, parent)
			{

				taskPool = new TaskPool(ErrorCallback);
			}


			protected virtual void ErrorCallback(List<KnownException> knownExceptions, AggregateException aggregate)
			{
				if (knownExceptions.Count > 0)
					ExceptionHandler.ReportException(new AggregateException(knownExceptions));

				if (aggregate != null)
					ExceptionHandler.ReportException(aggregate);
			}


			protected void EnqueueTask(Action action) =>
				taskPool.EnqueueTask(action);


			protected void EnqueueAction(Action action) =>
				taskPool.EnqueueAction(action);
		}
	}
}
