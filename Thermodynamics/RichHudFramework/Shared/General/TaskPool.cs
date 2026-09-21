using ParallelTasks;
using RichHudFramework.Internal;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace RichHudFramework
{
	public class AggregateException : Exception
	{
/// <summary>AggregateException operation.</summary>
		public AggregateException(string aggregatedMsg) : base(aggregatedMsg) { }

/// <summary>AggregateException operation.</summary>
		public AggregateException(IReadOnlyList<Exception> exceptions) : base(BuildMessage(exceptions)) { }

/// <summary>AggregateException operation.</summary>
		public AggregateException(IReadOnlyList<AggregateException> exceptions) : base(BuildMessage(exceptions)) { }

/// <summary>Builds the method table.</summary>
		private static string BuildMessage<T>(IReadOnlyList<T> exceptions) where T : Exception
		{
/// <summary>StringBuilder operation.</summary>
			var sb = new StringBuilder();

			for (int i = 0; i < exceptions.Count; i++)
			{
				sb.AppendLine(exceptions[i].ToString());
				if (i < exceptions.Count - 1)
					sb.AppendLine();
			}

			return sb.ToString();
		}
	}

	public class KnownException : Exception
	{
/// <summary>KnownException operation.</summary>
		public KnownException() : base() { }
/// <summary>KnownException operation.</summary>
		public KnownException(string message) : base(message) { }
/// <summary>KnownException operation.</summary>
		public KnownException(string message, Exception innerException) : base(message, innerException) { }
	}

	public class TaskPool : RichHudComponentBase
	{
		public static int MaxTasksRunning { get { return maxTasksRunning; } set { maxTasksRunning = MathHelper.Clamp(value, 1, 10); } }
        private static int maxTasksRunning = 1, tasksRunningCount = 0;

		private readonly List<Task> tasksRunning;
		private readonly Queue<Action> tasksWaiting;
		private readonly ConcurrentQueue<Action> actions;
		private readonly Action<List<KnownException>, AggregateException> errorCallback;

/// <summary>TaskPool operation.</summary>
		public TaskPool(Action<List<KnownException>, AggregateException> errorCallback) : base(true, true)
		{
			this.errorCallback = errorCallback;

/// <summary>List operation.</summary>
			tasksRunning = new List<Task>();
/// <summary>ConcurrentQueue operation.</summary>
			actions = new ConcurrentQueue<Action>();
/// <summary>Queue operation.</summary>
			tasksWaiting = new Queue<Action>();
		}

/// <summary>Close operation.</summary>
		public override void Close() => tasksRunningCount = 0;

/// <summary>Draw operation.</summary>
		public override void Draw()
		{
			TryStartWaitingTasks();
			UpdateRunningTasks();
			RunTaskActions();
		}

/// <summary>EnqueueTask operation.</summary>
		public void EnqueueTask(Action action)
		{
			if (Parent == null && RichHudCore.Instance != null)
				RegisterComponent(RichHudCore.Instance);
/// <summary>if operation.</summary>
			else if (ExceptionHandler.Unloading)
				throw new Exception("New tasks cannot be started while the mod is being unloaded.");

			tasksWaiting.Enqueue(action);
		}

/// <summary>EnqueueAction operation.</summary>
		public void EnqueueAction(Action action)
		{
			if (Parent == null && RichHudCore.Instance != null)
				RegisterComponent(RichHudCore.Instance);
/// <summary>if operation.</summary>
			else if (ExceptionHandler.Unloading)
				throw new Exception("New tasks cannot be started while the mod is being unloaded.");

			actions.Enqueue(action);
		}

/// <summary>TryStartWaitingTasks operation.</summary>
		private void TryStartWaitingTasks()
		{
			Action action;

			while (tasksRunningCount < maxTasksRunning && (tasksWaiting.Count > 0) && tasksWaiting.TryDequeue(out action))
			{
				tasksRunning.Add(MyAPIGateway.Parallel.Start(action));
				tasksRunningCount++;
			}
		}

/// <summary>UpdateRunningTasks operation.</summary>
		private void UpdateRunningTasks()
		{
/// <summary>List operation.</summary>
			List<KnownException> knownExceptions = new List<KnownException>();
/// <summary>List operation.</summary>
			List<Exception> otherExceptions = new List<Exception>(); //unknown exceptions
			AggregateException unknownExceptions = null;

			for (int n = 0; n < tasksRunning.Count; n++)
			{
				Task task = tasksRunning[n];

				if (task.Exceptions != null && task.Exceptions.Length > 0)
				{
					foreach (Exception exception in task.Exceptions)
					{
						if (exception is KnownException)
							knownExceptions.Add((KnownException)exception);
						else
							otherExceptions.Add(exception);
					}
				}

				if (!task.valid || task.IsComplete || (task.Exceptions != null && task.Exceptions.Length > 0))
				{
					tasksRunning.Remove(task);
					tasksRunningCount--;
				}
			}

			if (otherExceptions.Count > 0)
/// <summary>AggregateException operation.</summary>
				unknownExceptions = new AggregateException(otherExceptions);

			errorCallback(knownExceptions, unknownExceptions);
		}

/// <summary>RunTaskActions operation.</summary>
		private void RunTaskActions()
		{
			Action action;

			while (actions.Count > 0)
				if (actions.TryDequeue(out action))
					action();
		}
	}
}