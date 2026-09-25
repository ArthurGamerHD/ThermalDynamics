using System;
using System.Collections.Generic;
using System.Text;
using VRage;

namespace RichHudFramework
{
	public class StringBuilderPoolPolicy : IPooledObjectPolicy<StringBuilder>
	{

		public StringBuilder GetNewObject() => new StringBuilder();


		public void ResetObject(StringBuilder sb) => sb.Clear();


		public void ResetRange(IReadOnlyList<StringBuilder> objects, int index, int count)
		{
			int end = Math.Min(index + count, objects.Count);
			for (int i = index; i < end; i++)
				objects[i].Clear();
		}


		public void ResetRange<T2>(IReadOnlyList<MyTuple<StringBuilder, T2>> objects, int index, int count)
		{
			int end = Math.Min(index + count, objects.Count);
			for (int i = index; i < end; i++)
				objects[i].Item1.Clear();
		}


		public static ObjectPool<StringBuilder> GetNewPool() => new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());
	}
}