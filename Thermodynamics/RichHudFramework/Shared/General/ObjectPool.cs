using System;
using System.Collections.Generic;
using VRage;

namespace RichHudFramework
{
	public interface IPooledObjectPolicy<T>
	{
/// <summary>Returns the newobject.</summary>
		T GetNewObject();

/// <summary>ResetObject operation.</summary>
		void ResetObject(T obj);

/// <summary>ResetRange operation.</summary>
		void ResetRange(IReadOnlyList<T> objects, int index, int count);

/// <summary>ResetRange operation.</summary>
		void ResetRange<T2>(IReadOnlyList<MyTuple<T, T2>> objects, int index, int count);
	}

	public class PooledObjectPolicy<T> : IPooledObjectPolicy<T>
	{
		private readonly Func<T> getNewObjectFunc;
		private readonly Action<T> resetObjectAction;

/// <summary>PooledObjectPolicy operation.</summary>
		public PooledObjectPolicy(Func<T> getNewObjectFunc, Action<T> resetObjectAction)
		{
			if (getNewObjectFunc == null || resetObjectAction == null)
				throw new ArgumentNullException();

			this.getNewObjectFunc = getNewObjectFunc;
			this.resetObjectAction = resetObjectAction;
		}

/// <summary>Returns the newobject.</summary>
		public T GetNewObject() => getNewObjectFunc();
/// <summary>ResetObject operation.</summary>
		public void ResetObject(T obj) => resetObjectAction(obj);

/// <summary>ResetRange operation.</summary>
		public void ResetRange(IReadOnlyList<T> objects, int index, int count)
		{
			int end = Math.Min(index + count, objects.Count);
			for (int i = index; i < end; i++)
				resetObjectAction(objects[i]);
		}

/// <summary>ResetRange operation.</summary>
		public void ResetRange<T2>(IReadOnlyList<MyTuple<T, T2>> objects, int index, int count)
		{
			int end = Math.Min(index + count, objects.Count);
			for (int i = index; i < end; i++)
				resetObjectAction(objects[i].Item1);
		}
	}

	public class ObjectPool<T> where T : class
	{
		public int Count => pooledObjects.Count;

		public int Capacity => pooledObjects.Capacity;

		protected readonly List<T> pooledObjects;
		protected readonly IPooledObjectPolicy<T> policy;

/// <summary>ObjectPool operation.</summary>
		public ObjectPool(IPooledObjectPolicy<T> policy)
		{
			if (policy == null)
				throw new Exception("Pooled object policy cannot be null.");

/// <summary>List operation.</summary>
			pooledObjects = new List<T>();
			this.policy = policy;
		}

/// <summary>ObjectPool operation.</summary>
		public ObjectPool(Func<T> getNewFunc, Action<T> resetFunc)
			: this(new PooledObjectPolicy<T>(getNewFunc, resetFunc))
		{ }

/// <summary>Returns the .</summary>
		public T Get()
		{
			if (pooledObjects.Count > 0)
			{
				int last = pooledObjects.Count - 1;
				T obj = pooledObjects[last];
				pooledObjects.RemoveAt(last);
				return obj;
			}

			return policy.GetNewObject();
		}

/// <summary>Return operation.</summary>
		public void Return(T obj)
		{
			policy.ResetObject(obj);
			pooledObjects.Add(obj);
		}

/// <summary>ReturnRange operation.</summary>
		public void ReturnRange(IReadOnlyList<T> objects, int index = 0, int count = -1)
		{
			if (count == -1) count = objects.Count - index;
			if (count <= 0) return;

			policy.ResetRange(objects, index, count);

			int end = index + count - 1;
			for (int i = 0; i < count; i++)
				pooledObjects.Add(objects[end - i]);
		}

/// <summary>ReturnRange operation.</summary>
		public void ReturnRange<T2>(IReadOnlyList<MyTuple<T, T2>> objects, int index = 0, int count = -1)
		{
			if (count == -1) count = objects.Count - index;
			if (count <= 0) return;

			policy.ResetRange(objects, index, count);

			int end = index + count - 1;
			for (int i = 0; i < count; i++)
				pooledObjects.Add(objects[end - i].Item1);
		}

/// <summary>TrimExcess operation.</summary>
		public void TrimExcess() => pooledObjects.TrimExcess();
/// <summary>Clear operation.</summary>
		public void Clear() => pooledObjects.Clear();
	}
}