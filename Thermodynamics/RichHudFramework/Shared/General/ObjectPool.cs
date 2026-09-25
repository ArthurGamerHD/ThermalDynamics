using System;
using System.Collections.Generic;
using VRage;

namespace RichHudFramework
{
	public interface IPooledObjectPolicy<T>
	{

		T GetNewObject();


		void ResetObject(T obj);


		void ResetRange(IReadOnlyList<T> objects, int index, int count);


		void ResetRange<T2>(IReadOnlyList<MyTuple<T, T2>> objects, int index, int count);
	}

	public class PooledObjectPolicy<T> : IPooledObjectPolicy<T>
	{
		private readonly Func<T> getNewObjectFunc;
		private readonly Action<T> resetObjectAction;


		public PooledObjectPolicy(Func<T> getNewObjectFunc, Action<T> resetObjectAction)
		{
			if (getNewObjectFunc == null || resetObjectAction == null)
				throw new ArgumentNullException();

			this.getNewObjectFunc = getNewObjectFunc;
			this.resetObjectAction = resetObjectAction;
		}


		public T GetNewObject() => getNewObjectFunc();

		public void ResetObject(T obj) => resetObjectAction(obj);


		public void ResetRange(IReadOnlyList<T> objects, int index, int count)
		{
			int end = Math.Min(index + count, objects.Count);
			for (int i = index; i < end; i++)
				resetObjectAction(objects[i]);
		}


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


		public ObjectPool(IPooledObjectPolicy<T> policy)
		{
			if (policy == null)
				throw new Exception("Pooled object policy cannot be null.");


			pooledObjects = new List<T>();
			this.policy = policy;
		}


		public ObjectPool(Func<T> getNewFunc, Action<T> resetFunc)
			: this(new PooledObjectPolicy<T>(getNewFunc, resetFunc))
		{ }


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


		public void Return(T obj)
		{
			policy.ResetObject(obj);
			pooledObjects.Add(obj);
		}


		public void ReturnRange(IReadOnlyList<T> objects, int index = 0, int count = -1)
		{
			if (count == -1) count = objects.Count - index;
			if (count <= 0) return;

			policy.ResetRange(objects, index, count);

			int end = index + count - 1;
			for (int i = 0; i < count; i++)
				pooledObjects.Add(objects[end - i]);
		}


		public void ReturnRange<T2>(IReadOnlyList<MyTuple<T, T2>> objects, int index = 0, int count = -1)
		{
			if (count == -1) count = objects.Count - index;
			if (count <= 0) return;

			policy.ResetRange(objects, index, count);

			int end = index + count - 1;
			for (int i = 0; i < count; i++)
				pooledObjects.Add(objects[end - i].Item1);
		}


		public void TrimExcess() => pooledObjects.TrimExcess();

		public void Clear() => pooledObjects.Clear();
	}
}