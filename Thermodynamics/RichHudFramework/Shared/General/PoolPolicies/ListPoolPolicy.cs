using System;
using System.Collections.Generic;
using VRage;

namespace RichHudFramework
{
	public class ListPoolPolicy<T> : IPooledObjectPolicy<List<T>>
	{
/// <summary>Returns the newobject.</summary>
		public List<T> GetNewObject() => new List<T>();

/// <summary>ResetObject operation.</summary>
		public void ResetObject(List<T> list) => list.Clear();

/// <summary>ResetRange operation.</summary>
		public void ResetRange(IReadOnlyList<List<T>> lists, int index, int count)
		{
			int end = Math.Min(index + count, lists.Count);
			for (int i = index; i < end; i++)
				lists[i].Clear();
		}

/// <summary>ResetRange operation.</summary>
		public void ResetRange<T2>(IReadOnlyList<MyTuple<List<T>, T2>> lists, int index, int count)
		{
			int end = Math.Min(index + count, lists.Count);
			for (int i = index; i < end; i++)
				lists[i].Item1.Clear();
		}

/// <summary>Returns the newpool.</summary>
		public static ObjectPool<List<T>> GetNewPool() => new ObjectPool<List<T>>(new ListPoolPolicy<T>());
	}
}