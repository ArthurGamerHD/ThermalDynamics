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

		public AggregateException(string aggregatedMsg) : base(aggregatedMsg) { }


		public AggregateException(IReadOnlyList<Exception> exceptions) : base(BuildMessage(exceptions)) { }


		public AggregateException(IReadOnlyList<AggregateException> exceptions) : base(BuildMessage(exceptions)) { }


		private static string BuildMessage<T>(IReadOnlyList<T> exceptions) where T : Exception
		{

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

		public KnownException() : base() { }

		public KnownException(string message) : base(message) { }

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


		public TaskPool(Action<List<KnownException>, AggregateException> errorCallback) : base(true, true)
		{
			this.errorCallback = errorCallback;


			tasksRunning = new List<Task>();

			actions = new ConcurrentQueue<Action>();

			tasksWaiting = new Queue<Action>();
		}


		public override void Close() => tasksRunningCount = 0;


		public override void Draw()
		{
			TryStartWaitingTasks();
			UpdateRunningTasks();
			RunTaskActions();
		}


		public void EnqueueTask(Action action)
		{
			if (Parent == null && RichHudCore.Instance != null)
				RegisterComponent(RichHudCore.Instance);

			else if (ExceptionHandler.Unloading)
				throw new Exception("New tasks cannot be started while the mod is being unloaded.");

			tasksWaiting.Enqueue(action);
		}


		public void EnqueueAction(Action action)
		{
			if (Parent == null && RichHudCore.Instance != null)
				RegisterComponent(RichHudCore.Instance);

			else if (ExceptionHandler.Unloading)
				throw new Exception("New tasks cannot be started while the mod is being unloaded.");

			actions.Enqueue(action);
		}


		private void TryStartWaitingTasks()
		{
			Action action;

			while (tasksRunningCount < maxTasksRunning && (tasksWaiting.Count > 0) && tasksWaiting.TryDequeue(out action))
			{
				tasksRunning.Add(MyAPIGateway.Parallel.Start(action));
				tasksRunningCount++;
			}
		}


		private void UpdateRunningTasks()
		{

			List<KnownException> knownExceptions = new List<KnownException>();

			List<Exception> otherExceptions = new List<Exception>();
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

				unknownExceptions = new AggregateException(otherExceptions);

			errorCallback(knownExceptions, unknownExceptions);
		}


		private void RunTaskActions()
		{
			Action action;

			while (actions.Count > 0)
				if (actions.TryDequeue(out action))
					action();
		}
	}
}